module Server

open System
open Akka.Actor
open Akka.FSharp

open DomainMessages
open QueueTypes
open QueueActors

let verySimpleServerActor (mailbox: Actor<_>) =

    let rec loop () = actor {
        let! message = mailbox.Receive ()
        match message with
        | what ->
            printfn "Server: received request: %s" what
            mailbox.Sender() <! what
        return! loop ()
    }
    loop ()

let simpleServerActor (mailbox: Actor<_>) =

    let rec loop () = actor {
        let! message = mailbox.Receive ()
        match message with
        | Request what ->
            printfn "Server: received request: %s" what
            mailbox.Sender() <! Reply what
        | Reply what ->
            printfn "Server: I'm a server, not client, I ignore your %s" what
        return! loop ()
    }
    loop ()

let serverActor (mailbox: Actor<_>) =

    let acknowledger = spawn mailbox.Context "acknowledge" queueAcknowledgeActor
    let rec loop () = actor {
        let! message = mailbox.Receive ()
        match message with
        | Message (Request what) ->
            printfn "Server: received request: %s" what
            mailbox.Sender() <! Message (Reply what)
        | MessageWithAck (Request what, queue, tag) ->
            printfn "Server: received request: %s" what
            mailbox.Sender() <! Message (Reply what)
            acknowledger.Forward <| QueueAck (queue, tag)
        | _ -> printfn "Server: unsupported message: %A" message
        return! loop ()
    }
    loop ()
