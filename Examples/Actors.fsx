#if INTERACTIVE

#load "References.fsx"

open System
open Akka.Actor
open Akka.FSharp

open QueueTypes
open QueueActors
open DomainMessages
open Client
open Server

// Test actors

let system = System.create "system" <| Configuration.load ()
let server = spawn system "server" serverActor
let queue_factory = spawn system "queues" (queueFactoryActor {Hostname="localhost";Username="guest";Password="guest"})

let client = spawn system "client" (clientActor server)
let another_client = spawn system "another_client" (clientActor server)
let queue_reader = spawn system "queue_reader" (queueReaderActor<DomainMessage> Request client)
let another_queue_reader = spawn system "another_queue_reader" (queueReaderActor<DomainMessage> Request another_client)

// Work queue

let (queue : IActorRef) = queue_factory <? CreateExchange ("work", { QueueName = "hello"; ExchangeName = ""; ExchangeType = Direct; Durable = false; Arguments = null }, false) |> Async.RunSynchronously
queue <! Connect
queue <! Subscribe queue_reader
queue <! Publish (Content "Hi!")
queue <! Disconnect

// Pub/sub

queue_factory <! CreateExchange ("publisher", { QueueName = ""; ExchangeName = "logs"; ExchangeType = Fanout; Durable = false; Arguments = null }, true)
let publisher = select "akka://system/user/queues/publisher" system

publisher <! Connect
publisher <! Publish (Content "Hi 1!")

queue_factory <! CreateExchange ("subscriber", { QueueName = "logs"; ExchangeName = "logs"; ExchangeType = Direct; Durable = false; Arguments = null }, true)
let subscriber = select "akka://system/user/queues/subscriber" system

subscriber <! Connect
subscriber <! Subscribe another_queue_reader
publisher <! Publish (Content "Hi 2!")
subscriber <! Disconnect

publisher <! Publish (Content "Hi 3!")
publisher <! Disconnect

// Routing

queue_factory <! CreateExchange ("routing_publisher", { QueueName = ""; ExchangeName = "direct_logs"; ExchangeType = Topic; Durable = false; Arguments = null }, true)
let routing_publisher = select "akka://system/user/queues/routing_publisher" system

routing_publisher <! Connect
routing_publisher <! Publish (ContentWithRouting ("Hi 1!", "a"))

queue_factory <! CreateExchange ("routing_subscriber1", { QueueName = "direct_logs"; ExchangeName = "direct_logs"; ExchangeType = Topic; Durable = false; Arguments = null }, true)
queue_factory <! CreateExchange ("routing_subscriber2", { QueueName = "direct_logs"; ExchangeName = "direct_logs"; ExchangeType = Topic; Durable = false; Arguments = null }, true)
let routing_subscriber1 = select "akka://system/user/queues/routing_subscriber1" system
let routing_subscriber2 = select "akka://system/user/queues/routing_subscriber2" system

routing_subscriber1 <! Connect
routing_subscriber1 <! Subscribe queue_reader
routing_subscriber1 <! Route "a"

routing_subscriber2 <! Connect
routing_subscriber2 <! Subscribe another_queue_reader
routing_subscriber2 <! Route "a"
routing_subscriber2 <! Route "b"

routing_publisher <! Publish (ContentWithRouting ("Hi 2!", "a"))
routing_publisher <! Publish (ContentWithRouting ("Hi 3!", "b"))
routing_subscriber1 <! Unsubscribe
routing_subscriber2 <! Unroute "b"

routing_publisher <! Publish (ContentWithRouting ("Hi 4!", "a"))
routing_publisher <! Publish (ContentWithRouting ("Hi 5!", "b"))
routing_subscriber2 <! Unsubscribe

routing_publisher <! Disconnect
routing_subscriber1 <! Disconnect
routing_subscriber2 <! Disconnect

// Topic

queue_factory <! CreateExchange ("topic_publisher", { QueueName = ""; ExchangeName = "topic_logs"; ExchangeType = Topic; Durable = false; Arguments = null }, true)
let topic_publisher = select "akka://system/user/queues/topic_publisher" system

topic_publisher <! Connect
topic_publisher <! Publish (ContentWithRouting ("Hi 1!", "x.y"))

queue_factory <! CreateExchange ("topic_subscriber", { QueueName = "topic_logs"; ExchangeName = "topic_logs"; ExchangeType = Topic; Durable = false; Arguments = null }, true)
let topic_subscriber = select "akka://system/user/queues/topic_subscriber" system

topic_subscriber <! Connect
topic_subscriber <! Subscribe queue_reader
topic_subscriber <! Route "x.*"
topic_publisher <! Publish (ContentWithRouting ("Hi 2!", "x"))
topic_publisher <! Publish (ContentWithRouting ("Hi 3!", "x.y"))
topic_subscriber <! Route "x"
topic_publisher <! Publish (ContentWithRouting ("Hi 4!", "x"))
topic_subscriber <! Route "#"
topic_publisher <! Publish (ContentWithRouting ("Hi 5!", "y"))
topic_subscriber <! Disconnect

topic_publisher <! Publish (ContentWithRouting ("Hi 6!", "x.y"))
topic_publisher <! Disconnect

#endif
