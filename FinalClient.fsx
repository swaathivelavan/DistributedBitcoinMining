#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
open System
open System.IO
open System.Security.Cryptography
open System.Text.Encodings
open System.Threading
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

let serverip = fsi.CommandLineArgs.[1] |> string
let serverport = fsi.CommandLineArgs.[2] |>string
let addr = "akka.tcp://RemoteFSharp@" + serverip + ":" + serverport + "/user/server"
let mutable count=0L //to keep track of the workers
let workers = System.Environment.ProcessorCount |> int64

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""                
            }
            remote {
                helios.tcp {
                    port = 8777
                    hostname = localhost
                }
            }
        }")

let system = ActorSystem.Create("ClientFsharp", configuration)

let echoServer = 
    spawn system "EchoServer"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match box message with
                | :? string -> 
                        printfn "super!"
                        sender <! sprintf "Hello %s remote" message
                        return! loop()
                | _ ->  failwith "unknown message"
            } 
        loop()

type ActorMsg =
    | WorkerMsg of int64*int64*int64
    | DispatcherMsg of int64*int64
    | EndMsg of int64

let mutable ref = null

//function to check if random string has desired number of leading zeros
let checkLeadingZeros start k  =
    let mutable sum = 0L
    let mutable flag=false    
    let chars = "ABCDEFGHIJKLMNOPQRSTUVWUXYZabcdefghijklmnopqrstuvwxyz0123456789"
    let charsLen = chars.Length
    let random = System.Random()
    let arbitrary=int(k)
    let randomChars = [|for i in 0..40 -> chars.[random.Next(charsLen)]|]
    let ra = String(randomChars)
    let text = "k.iyer" + ra // random input string prefixed by gator id
    let bytes=System.Text.Encoding.ASCII.GetBytes text;
    let hash=SHA256.Create().ComputeHash(bytes);
    let ByteToHex (bytes:byte[]) = // function to convert byte to hash code
        bytes |> Array.fold (fun state x-> state + sprintf "%02X" x) ""
    let arr=ByteToHex hash
    let mutable count = 0;
    for i=0 to arbitrary-1 do 
        if arr.[i]='0' then count<-count+1
    if count=arbitrary then flag<-true
    if flag=true then 
        printfn "Found in CLIENT %A" arr

        let echoClient = system.ActorSelection(addr) //sending found result to server
        let msgToServer = "Client : String- " + (text|>string) + "    SHA256- " + (arr|>string)
        echoClient <! msgToServer

let LeadingZeros (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | WorkerMsg(start,endI,k) ->    for i in start .. workers .. endI do
                                            checkLeadingZeros start k 
                                        mailbox.Sender() <! EndMsg(start)
        | _ -> printfn "Worker Received Wrong message"
    }
    loop()

let mutable localWorkDone = false
let Dispatcher (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | DispatcherMsg(N,k) -> let workersList=[for a in 1L .. workers do yield(spawn system ("Job" + (string a)) LeadingZeros)]
                                printfn "Received Dispatcher command from server : "
                                printfn "Running Dispatcher" 
                                let effort = N/(workers-1L)
                                for i in 0L .. (workers-1L) do
                                    workersList.Item(i|>int) <! WorkerMsg( 1L, N , k )
                                            
        | EndMsg(index) ->  count <- count+1L
                            if count = workers then
                                localWorkDone <- true
                                ref <! "PD"
        | _ -> printfn "Dispatcher Received Wrong message"
        return! loop()
    }
    loop()


let mutable remoteWorkDone = false
let commlink = 
    spawn system "client"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! msg = mailbox.Receive()
                let response =msg|>string
                let command = (response).Split ','
                if command.[0].CompareTo("init")=0 then
                    let echoClient = system.ActorSelection(addr)
                    let msgToServer = "Job," + (workers|>string)
                    echoClient <! msgToServer
                
                elif command.[0].CompareTo("Hello")=0 then
                    let DispatcherRef = spawn system "Dispatcher" Dispatcher
                    DispatcherRef <! DispatcherMsg( command.[1] |> int64 , command.[2] |> int64)
                    ref <- mailbox.Sender()
                    
                elif response.CompareTo("ProcessingDone")=0 then
                    system.Terminate() |> ignore
                    remoteWorkDone <- true
                
                else
                    printfn "-Found in Server : %s-" msg

                return! loop() 
            }
        loop()

commlink <! "init"

while (not localWorkDone && not remoteWorkDone) do

system.WhenTerminated.Wait()