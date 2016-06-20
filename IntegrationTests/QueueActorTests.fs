module QueueActorTests

open System
open Akka.Actor
open Akka.FSharp
open QueueTypes
open QueueActors
open NUnit.Framework

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

let wait numberOfSeconds = System.Threading.Thread.Sleep(numberOfSeconds * 1000)

let startSystem () =
    let system = System.create "system" <| Configuration.load ()
    let queueFactory = spawn system "queues" (queueFactoryActor {Hostname="localhost";Username="guest";Password="guest"})
    (system, queueFactory)

let stopSystem (system : ActorSystem) =
    system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously

let createDirectQueue queueFactory queueName exchangeName =
    let queueParams = { QueueName = queueName; ExchangeName = exchangeName; ExchangeType = Direct; Durable = false; Arguments = null }
    let (queue : IActorRef) = queueFactory <? CreateExchange (sprintf "direct_%s" queueName, queueParams, false) |> Async.RunSynchronously
    queue

let createFanoutExchange queueFactory exchangeName =
    let exchangeParams = { QueueName = ""; ExchangeName = exchangeName; ExchangeType = Fanout; Durable = false; Arguments = null }
    let (exchange : IActorRef) = queueFactory <? CreateExchange (sprintf "fanout_%s" exchangeName, exchangeParams, true) |> Async.RunSynchronously
    exchange

let createSubscriber system subcriberName =
    let accumulator = spawn system (sprintf "accumulator_%s" subcriberName) accumulatorActor
    let subscriber = spawn system subcriberName (queueReaderActor<string> id accumulator)
    (subscriber, accumulator)

[<Test>]
let ``Work queue``() = 
    let (system, queueFactory) = startSystem ()
    let queue = createDirectQueue queueFactory "hello" ""

    let (subscriber, accumulator) = createSubscriber system "subscriber"

    queue <! Connect
    queue <! Subscribe subscriber
    queue <! Publish (Content "Hi!")
    wait 1
    queue <! Disconnect

    let (messages : string list) = accumulator <? Query |> Async.RunSynchronously
    Assert.AreEqual(["Hi!"], messages)
    stopSystem system

[<Test>]
let ``Pub/sub``() = 
    let (system, queueFactory) = startSystem ()
    let exchange = createFanoutExchange queueFactory "logs"
    let queue1 = createDirectQueue queueFactory "logs1" "logs"
    let queue2 = createDirectQueue queueFactory "logs2" "logs"

    let (subscriber1, accumulator1) = createSubscriber system "subscriber1"
    let (subscriber2, accumulator2) = createSubscriber system "subscriber2"

    exchange <! Connect
    queue1 <! Connect
    queue2 <! Connect
    queue1 <! Subscribe subscriber1
    exchange <! Publish (Content "Hi 1!")
    queue2 <! Subscribe subscriber2
    exchange <! Publish (Content "Hi 2!")
    wait 1
    exchange <! Disconnect
    queue1 <! Disconnect
    queue2 <! Disconnect

    let (messages : string list) = accumulator1 <? Query |> Async.RunSynchronously
    Assert.AreEqual(["Hi 2!"; "Hi 1!"], messages)
    let (messages : string list) = accumulator2 <? Query |> Async.RunSynchronously
    Assert.AreEqual(["Hi 2!"; "Hi 1!"], messages)
    stopSystem system
