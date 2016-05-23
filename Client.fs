module Client

open System
open System.Text
open Akka.Actor
open Akka.FSharp

open DomainMessages
open QueueActors

let simpleClientActor (server: IActorRef) (mailbox: Actor<_>) =

    let rec loop () =
        actor {
            let! message = mailbox.Receive ()
            match message with
            | Request what ->
                printfn "Client: sending request: %s" what
                server <! message
            | Reply what -> 
                printfn "Client: received reply: %s" what
            return! loop ()
        }

    loop ()

let clientActor (server: IActorRef) (mailbox: Actor<_>) =

    let rec loop () =
        actor {
            let! message = mailbox.Receive ()
            match message with
            | Message (Request what) ->
                printfn "Client: sending request: %s" what
                server <! message
            | MessageWithAck (Request what, queue, tag) -> 
                printfn "Client: sending request: %s" what
                server <! message
            | Message (Reply what) -> 
                printfn "Client: received reply: %s" what
            | _ -> printfn "Client: unsupported message: %A" message
            return! loop ()
        }

    loop ()
