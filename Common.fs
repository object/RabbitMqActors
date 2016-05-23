[<AutoOpen>]
module Common

open Akka.Actor

type AckId = uint64

type MessageEnvelope<'a> =
    | Message of 'a
    | MessageWithAck of 'a * IActorRef * AckId
