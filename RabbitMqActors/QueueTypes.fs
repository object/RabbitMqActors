module QueueTypes

    open System.Collections.Generic
    open Akka.Actor

    type ConnectionDetails =
        { Hostname: string
          Username: string
          Password: string }

    type ExchangeType =
        | Direct
        | Fanout
        | Topic

    type QueueExchange = 
        { QueueName: string
          ExchangeName: string
          ExchangeType: ExchangeType
          Durable: bool
          Arguments: IDictionary<string,obj> }

    type Payload = string
    type RoutingKey = string

    type MessageContent =
        | Content of Payload
        | ContentWithRouting of Payload * RoutingKey

    type FactoryCommand =
        | CreateExchange of string * QueueExchange * bool

    type QueueCommand =
        | Connect
        | ConnectWithQos of uint16 * uint16
        | Disconnect
        | Subscribe of IActorRef
        | Unsubscribe
        | Route of RoutingKey
        | Unroute of RoutingKey
        | Publish of MessageContent
        | Receive of AckId * Payload
        | Ack of AckId
        | Nack of AckId

    type SubscriberMessage =
        | StartWith of IActorRef
        | Stop
        | QueueMessage of string
        | QueueMessageWithAck of AckId * string

    type AcknowledgementMessage = 
        | QueueAck of IActorRef * AckId
        | QueueNack of IActorRef * AckId
