module QueueActors

    open System
    open System.Text
    open Akka.Actor
    open Akka.FSharp
    open RabbitMQ.Client
    open RabbitMQ.Client.Events
    open RabbitMQ.Client.Exceptions

    open QueueTypes

    let queueActor (factory : ConnectionFactory) (queueExchange : QueueExchange * bool) (mailbox: Actor<_>) =

        let (queueExchange, noAck) = queueExchange

        let declareExchange (channel : IModel) queueExchange =
            let exchangeName = (sprintf "%A" queueExchange.ExchangeType).ToLower()
            channel.ExchangeDeclare(queueExchange.ExchangeName, exchangeName, queueExchange.Durable, false, queueExchange.Arguments)

        let declareQueue (channel : IModel) queueExchange =
            match queueExchange.QueueName with
            | "" | null -> 
                let queue = channel.QueueDeclare()
                channel.QueueBind(queue.QueueName, queueExchange.ExchangeName, "")
            | _ -> 
                channel.QueueDeclare(queueExchange.QueueName, queueExchange.Durable, false, false, queueExchange.Arguments) |> ignore
                if not(String.IsNullOrEmpty(queueExchange.ExchangeName)) then 
                    channel.QueueBind(queueExchange.QueueName, queueExchange.ExchangeName, "")

        let connect (prefetchCountPerChannel, prefetchCountPerConsumer) =
            let connection = factory.CreateConnection()
            let channel = connection.CreateModel()
            if prefetchCountPerChannel > 0us then channel.BasicQos(0u, prefetchCountPerChannel, true)
            if prefetchCountPerConsumer > 0us then channel.BasicQos(0u, prefetchCountPerConsumer, false)

            match queueExchange.ExchangeType with
            | Direct -> 
                declareQueue channel queueExchange
            | Topic -> 
                declareExchange channel queueExchange
                declareQueue channel queueExchange
            | Fanout -> 
                declareExchange channel queueExchange
            (connection, channel)

        let disconnect (connection : IConnection) (channel : IModel) =
            channel.Dispose()
            connection.Dispose()

        let publishMessage (channel : IModel) (message : MessageContent) =
            let (what, routingKey) =
                match message with
                | Content what -> (what, "")
                | ContentWithRouting (what, routingKey)-> (what, routingKey)
            let body = Encoding.UTF8.GetBytes(what)
            match queueExchange.ExchangeType with
            | ExchangeType.Direct -> channel.BasicPublish(queueExchange.ExchangeName, queueExchange.QueueName, null, body)
            | _ -> channel.BasicPublish(queueExchange.ExchangeName, routingKey, null, body)

        let receiveMessage (e : BasicDeliverEventArgs) =
            let message = Encoding.UTF8.GetString(e.Body)
            mailbox.Self <! Receive (e.DeliveryTag, message)

        let rec disconnected () = 
            actor {
                let! message = mailbox.Receive ()
                match message with
                | Connect -> 
                    let (connection, channel) = connect (0us, 0us)
                    return! connected (connection, channel)

                | ConnectWithQos (prefetchCountPerChannel, prefetchCountPerConsumer) -> 
                    let (connection, channel) = connect (prefetchCountPerChannel, prefetchCountPerConsumer)
                    return! connected (connection, channel)

                | _ -> 
                    printfn "Queue: invalid operation in disconnected state: %A" message
                    return! disconnected ()
            }
        and connected (connection : IConnection, channel : IModel) = 
            actor {
                let! message = mailbox.Receive ()
                match message with
                | Subscribe subscriber ->
                    let consumer = new EventingBasicConsumer(channel)
                    consumer.Received.Add(receiveMessage)
                    subscriber <! StartWith mailbox.Self
                    channel.BasicConsume(queueExchange.QueueName, noAck, consumer) |> ignore
                    return! subscribed (connection, channel, subscriber)

                | Disconnect ->
                    disconnect connection channel
                    return! disconnected ()

                | Publish content -> publishMessage channel content

                | Receive _ -> ()

                | Ack tag -> channel.BasicAck(tag, false)

                | Nack tag -> channel.BasicNack(tag, false, false)

                | _ -> 
                    printfn "Queue: invalid operation in connected state: %A" message

                return! connected (connection, channel)
            }
        and subscribed (connection : IConnection, channel : IModel, subscriber : IActorRef) = 
            actor {
                let! message = mailbox.Receive ()
                match message with
                | Unsubscribe ->
                    return! connected (connection, channel)
                
                | Route routingKey ->
                    match queueExchange.ExchangeType with
                    | Direct -> printfn "Queue: Can not set routing for Direct exchange type."
                    | _ -> 
                        channel.QueueBind(queueExchange.QueueName, queueExchange.ExchangeName, routingKey)

                | Unroute routingKey ->
                    match queueExchange.ExchangeType with
                    | Direct -> printfn "Queue: Can not set routing for Direct exchange type."
                    | _ -> 
                        channel.QueueUnbind(queueExchange.QueueName, queueExchange.ExchangeName, routingKey, null)

                | Disconnect ->
                    subscriber <! Stop
                    disconnect connection channel
                    return! disconnected ()

                | Publish content -> publishMessage channel content

                | Receive (tag, what) ->
                    match noAck with
                    | false -> subscriber <! QueueMessageWithAck (tag, what)
                    | true -> subscriber <! QueueMessage what

                | Ack tag -> channel.BasicAck(tag, false)

                | Nack tag -> channel.BasicNack(tag, false, false)

                | _ -> 
                    printfn "Queue: invalid operation in connected state: %A" message

                return! subscribed (connection, channel, subscriber)
            }

        disconnected ()

    let queueFactoryActor (connectionDetails : ConnectionDetails) (mailbox: Actor<_>) =
        let connectionFactory = new ConnectionFactory(HostName = connectionDetails.Hostname,
                                                      UserName = connectionDetails.Username, 
                                                      Password = connectionDetails.Password)
        connectionFactory.AutomaticRecoveryEnabled <- true

        let rec loop (connectionFactory : ConnectionFactory) =
            actor {
                let! message = mailbox.Receive ()
                match message with
                | CreateExchange (actorName, queueExchange, noAck) ->
                    let actor = spawn mailbox.Context actorName (queueActor connectionFactory (queueExchange, noAck))
                    mailbox.Sender() <! actor

                return! loop (connectionFactory)
            }

        loop (connectionFactory)

    let queueAcknowledgeActor (mailbox: Actor<_>) =
        let rec loop () = actor {
            let! message = mailbox.Receive ()
            match message with
            | QueueAck (queue, tag) -> queue <! Ack tag
            | QueueNack (queue, tag) -> queue <! Nack tag
            return! loop ()
        }
        loop ()

    let queueReaderActor<'a> (deserializeMessage : (string -> 'a)) (messageHandler: IActorRef) (mailbox: Actor<_>) =
        let rec starting() =
            actor {
                let! message = mailbox.Receive ()
                match message with
                | StartWith queue ->
                    return! listening (queue)
                | _ -> 
                    printfn "Client: invalid operation in starting state: %A" message
                return! starting ()
            }
        and listening (queue: IActorRef) = 
            actor {
                let! message = mailbox.Receive ()
                match message with
                | StartWith queue ->
                    return! listening (queue)
                | Stop ->
                    return! starting ()
                | QueueMessage msg ->
                    let message : MessageEnvelope<'a> = Message(deserializeMessage msg)
                    messageHandler <! message
                | QueueMessageWithAck (tag, msg) ->
                    let message : MessageEnvelope<'a> = MessageWithAck(deserializeMessage msg, queue, tag)
                    messageHandler <! message
                return! listening (queue)
            }

        starting ()

    let publishMessage<'a> (serializeMessage : ('a -> string)) (queue: IActorRef) (message : 'a) =
        let content = serializeMessage message
        queue <! Publish (content |> Content)

    let queuePublisher<'a> (serializeMessage : ('a -> string)) (queue: IActorRef) =
        publishMessage<'a> serializeMessage queue
