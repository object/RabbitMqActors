module QueueActorTests

open System
open Akka.Actor
open Akka.FSharp
open QueueTypes
open QueueActors
open NUnit.Framework

let wait (milliseconds : int) = System.Threading.Thread.Sleep(milliseconds)

type AccumulatorRequest = Query

let accumulatorActor (mailbox: Actor<obj>) =
    let acknowledger = spawn mailbox.Context "acknowledge" queueAcknowledgeActor
    let rec loop (msgs : string list) = actor {
        let! message = mailbox.Receive ()
        match message with
        | :? AccumulatorRequest -> mailbox.Sender() <! msgs; return! loop (msgs)
        | :? MessageEnvelope<string> as msg ->
            match msg with
            | MessageWithAck(msg,queue,tag) -> 
                acknowledger.Forward <| QueueAck (queue, tag)
                return! loop (msg :: msgs)
            | Message msg -> 
                return! loop (msg :: msgs)
        return! loop (msgs)
    }
    loop ([])

let startSystem () =
    let system = System.create "system" <| Configuration.load ()
    let queueFactory = spawn system "queues" (queueFactoryActor {Hostname="localhost";Username="guest";Password="guest"})
    (system, queueFactory)

let stopSystem (system : ActorSystem) =
    system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously

let createQueue queueFactory queueName exchangeName exchangeType =
    let queueParams = { QueueName = queueName; ExchangeName = exchangeName; ExchangeType = exchangeType; Durable = false; Arguments = null }
    let (queue : IActorRef) = queueFactory <? CreateExchange (sprintf "queue_%s" queueName, queueParams, false) |> Async.RunSynchronously
    wait 100
    queue

let createExchange queueFactory exchangeName exchangeType =
    let exchangeParams = { QueueName = ""; ExchangeName = exchangeName; ExchangeType = exchangeType; Durable = false; Arguments = null }
    let (exchange : IActorRef) = queueFactory <? CreateExchange (sprintf "exchange_%s" exchangeName, exchangeParams, true) |> Async.RunSynchronously
    wait 100
    exchange

let createSubscriber system subcriberName =
    let accumulator = spawn system (sprintf "accumulator_%s" subcriberName) accumulatorActor
    let subscriber = spawn system subcriberName (queueReaderActor<string> id accumulator)
    (subscriber, accumulator)

let connect xs =
    xs |> Seq.iter (fun x -> x <! Connect)

let disconnect xs =
    wait 1000
    xs |> Seq.iter (fun x -> x <! Disconnect)

let subscribe xs =
    xs |> Seq.iter (fun (x,y) -> x <! Subscribe y)

let validate accumulator msgs =
    let (messages : string list) = accumulator <? Query |> Async.RunSynchronously
    Assert.AreEqual(msgs, messages)

[<Test>]
let ``Work queue``() = 
    let (system, queueFactory) = startSystem ()
    let queue = createQueue queueFactory "hello" "" Direct

    let (subscriber, accumulator) = createSubscriber system "subscriber"

    connect [queue]
    subscribe [(queue, subscriber)]

    queue <! Publish (Content "Hi!")

    disconnect [queue]

    validate accumulator ["Hi!"]
    stopSystem system

[<Test>]
let ``Pub/sub``() = 
    let (system, queueFactory) = startSystem ()
    let exchange = createExchange queueFactory "fanout_logs" Fanout
    let queue1 = createQueue queueFactory "fanout_logs1" "fanout_logs" Direct
    let queue2 = createQueue queueFactory "fanout_logs2" "fanout_logs" Direct

    let (subscriber1, accumulator1) = createSubscriber system "subscriber1"
    let (subscriber2, accumulator2) = createSubscriber system "subscriber2"

    connect [exchange; queue1; queue2]
    subscribe [(queue1, subscriber1); (queue2, subscriber2)]

    exchange <! Publish (Content "Hi 1!")
    exchange <! Publish (Content "Hi 2!")

    disconnect [exchange; queue1; queue2]

    validate accumulator1 ["Hi 2!"; "Hi 1!"]
    validate accumulator2 ["Hi 2!"; "Hi 1!"]
    stopSystem system

[<Test>]
let ``Routing``() = 
    let (system, queueFactory) = startSystem ()
    let exchange = createExchange queueFactory "routing_logs" Topic
    let queue1 = createQueue queueFactory "routing_logs1" "routing_logs" Topic
    let queue2 = createQueue queueFactory "routing_logs2" "routing_logs" Topic

    let (subscriber1, accumulator1) = createSubscriber system "subscriber1"
    let (subscriber2, accumulator2) = createSubscriber system "subscriber2"

    connect [exchange; queue1; queue2]
    subscribe [(queue1, subscriber1); (queue2, subscriber2)]

    queue1 <! Route "a"
    queue2 <! Route "b"

    exchange <! Publish (Content "Hi 1!")
    exchange <! Publish (ContentWithRouting ("Hi 2!", "a"))
    exchange <! Publish (ContentWithRouting ("Hi 3!", "b"))
    queue2 <! Unroute "b"
    queue1 <! Route "b"
    exchange <! Publish (ContentWithRouting ("Hi 4!", "b"))

    disconnect [exchange; queue1; queue2]

    validate accumulator1 ["Hi 4!"; "Hi 3!"; "Hi 2!"; "Hi 1!"]
    validate accumulator2 ["Hi 1!"]
    stopSystem system

[<Test>]
let ``Topic``() = 
    let (system, queueFactory) = startSystem ()
    let exchange = createExchange queueFactory "topic_logs" Topic
    let queue1 = createQueue queueFactory "topic_logs1" "topic_logs" Topic
    let queue2 = createQueue queueFactory "topic_logs2" "topic_logs" Topic
    let queue3 = createQueue queueFactory "topic_logs3" "topic_logs" Topic
    let queue4 = createQueue queueFactory "topic_logs4" "topic_logs" Topic

    let (subscriber1, accumulator1) = createSubscriber system "subscriber1"
    let (subscriber2, accumulator2) = createSubscriber system "subscriber2"
    let (subscriber3, accumulator3) = createSubscriber system "subscriber3"
    let (subscriber4, accumulator4) = createSubscriber system "subscriber4"

    connect [exchange; queue1; queue2; queue3; queue4]
    subscribe [(queue1, subscriber1); (queue2, subscriber2); (queue3, subscriber3); (queue4, subscriber4)]

    queue1 <! Route "x."
    queue2 <! Route "x"
    queue3 <! Route "y"
    queue4 <! Route "#"

    exchange <! Publish (Content "Hi 1!")
    exchange <! Publish (ContentWithRouting ("Hi 2!", "x"))
    exchange <! Publish (ContentWithRouting ("Hi 3!", "x.y"))
    exchange <! Publish (ContentWithRouting ("Hi 4!", "y"))

    disconnect [exchange; queue1; queue2; queue3; queue4]

    validate accumulator1 ["Hi 1!"]
    validate accumulator2 ["Hi 2!"; "Hi 1!"]
    validate accumulator3 ["Hi 4!"; "Hi 1!"]
    validate accumulator4 ["Hi 4!"; "Hi 3!"; "Hi 2!"; "Hi 1!"]
    stopSystem system
