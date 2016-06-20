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

[<Test>]
let ``Work queue``() = 
    let system = System.create "system" <| Configuration.load ()
    let queuefactory = spawn system "queues" (queueFactoryActor {Hostname="localhost";Username="guest";Password="guest"})
    let accumulator = spawn system "accumulator" accumulatorActor
    let subscriber = spawn system "subscriber" (queueReaderActor<string> id accumulator)
    let queueParams = { QueueName = "hello"; ExchangeName = ""; ExchangeType = Direct; Durable = false; Arguments = null }
    let (queue : IActorRef) = queuefactory <? CreateExchange ("work", queueParams, false) |> Async.RunSynchronously

    queue <! Connect
    queue <! Subscribe subscriber
    queue <! Publish (Content "Hi!")
    wait 1
    queue <! Disconnect

    let (messages : string list) = accumulator <? Query |> Async.RunSynchronously
    Assert.AreEqual(["Hi!"], messages)
    system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously

[<Test>]
let ``Pub/sub``() = 
    let system = System.create "system" <| Configuration.load ()
    let queuefactory = spawn system "queues" (queueFactoryActor {Hostname="localhost";Username="guest";Password="guest"})
    let accumulator1 = spawn system "accumulator1" accumulatorActor
    let subscriber1 = spawn system "subscriber1" (queueReaderActor<string> id accumulator1)
    let accumulator2 = spawn system "accumulator2" accumulatorActor
    let subscriber2 = spawn system "subscriber2" (queueReaderActor<string> id accumulator2)
    let exchangeParams = { QueueName = ""; ExchangeName = "logs"; ExchangeType = Fanout; Durable = false; Arguments = null }
    let (exchange : IActorRef) = queuefactory <? CreateExchange ("publisher", exchangeParams, true) |> Async.RunSynchronously
    let queueParams = { QueueName = "logs1"; ExchangeName = "logs"; ExchangeType = Direct; Durable = false; Arguments = null }
    let (queue1 : IActorRef) = queuefactory <? CreateExchange ("subscriber1", queueParams, true) |> Async.RunSynchronously
    let queueParams = { QueueName = "logs2"; ExchangeName = "logs"; ExchangeType = Direct; Durable = false; Arguments = null }
    let (queue2 : IActorRef) = queuefactory <? CreateExchange ("subscriber2", queueParams, true) |> Async.RunSynchronously

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
    system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously
