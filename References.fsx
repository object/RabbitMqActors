#I @"./packages/Newtonsoft.Json.7.0.1/lib/net45"
#I @"./packages/Akka.1.0.8/lib/net45"
#I @"./packages/Akka.FSharp.1.0.8/lib/net45"
#I @"./packages/FsPickler.1.2.21/lib/net45"
#I @"./packages/System.Collections.Immutable.1.1.36/lib/portable-net45+win8+wp8+wpa81" 

// Mono has a build in version of RabbitMQ that takes
// precedance unless with fully quality the path to the client
// library that we really want to load
#r "./packages/RabbitMQ.Client.3.6.2/lib/net45/RabbitMQ.Client.dll"
#r "System.Collections.Immutable.dll"
#r "Akka.dll"
#r "Akka.FSharp.dll"

#load "Common.fs"
#load "QueueTypes.fs"
#load "QueueActors.fs"
#load "DomainMessages.fs"
#load "Client.fs"
#load "Server.fs"

