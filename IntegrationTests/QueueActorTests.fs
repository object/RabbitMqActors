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
    let queueReader = spawn system "queue_reader" (queueReaderActor<string> id accumulator)
    let exchangeParams = { QueueName = "hello"; ExchangeName = ""; ExchangeType = Direct; Durable = false; Arguments = null }
    let (queue : IActorRef) = queuefactory <? CreateExchange ("work", exchangeParams, false) |> Async.RunSynchronously

    queue <! Connect
    queue <! Subscribe queueReader
    queue <! Publish (Content "Hi!")
    wait 1
    queue <! Disconnect

    let (messages : string list) = accumulator <? Query |> Async.RunSynchronously
    Assert.AreEqual(["Hi!"], messages)
    system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously
