#if INTERACTIVE

#I @"../packages\Newtonsoft.Json\lib\net45"
#I @"../packages/Akka/lib/net45"
#I @"../packages/Akka.FSharp/lib/net45"
#I @"../packages/FsPickler/lib/net45"
#I @"../packages/System.Collections.Immutable/lib/portable-net45+win8+wp8+wpa81" 

// Mono has a build in version of RabbitMQ that takes
// precedance unless with fully quality the path to the client
// library that we really want to load
#r "../packages/RabbitMQ.Client/lib/net45/RabbitMQ.Client.dll"
#r "System.Collections.Immutable.dll"
#r "Akka.dll"
#r "Akka.FSharp.dll"

#load "../RabbitMqActors/Common.fs"
#load "../RabbitMqActors/QueueTypes.fs"
#load "../RabbitMqActors/QueueActors.fs"
#load "DomainMessages.fs"
#load "Client.fs"
#load "Server.fs"

#endif
