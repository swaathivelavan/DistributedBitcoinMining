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

let N = fsi.CommandLineArgs.[1] |> int64 // Receive the number of tries to be made 
let k = fsi.CommandLineArgs.[2] |> int64 // Receive the number of leading zeros to be found

let mutable client= false //checking presence of client
let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8777
                    hostname = 192.168.0.206
                }
            }
        }")

let mutable remoteWorkDone = false //Work done by client
let mutable localWorkDone = false //Work done by server

type ActorMsg =
    | WorkerMsg of int64*int64*int64
    | DispatcherMsg of int64*int64*int64
    | EndMsg of int64
    | ResultMsg of int64

let mutable count=0L //to keep track of the workers
let workers = System.Environment.ProcessorCount |> int64 //As many workers as cores
let system = ActorSystem.Create("RemoteFSharp", configuration)
let mutable ref = null  // Message that would be sent 

let checkLeadingZeros start k  =
    //let mutable sum = 0L
    let mutable flag=false // To check if it has the desired number of leading zeros
    //Creating random strings
    let chars = "ABCDEFGHIJKLMNOPQRSTUVWUXYZabcfefghijklmnopqrstuvwxyz0123456789"
    let charsLen = chars.Length
    let random = System.Random()
    let arbitrary=int(k)
    let randomChars = [|for i in 0..40 -> chars.[random.Next(charsLen)]|]
    let ra = String(randomChars)
    let text = "k.iyer" + ra // UFL ID+ random string 
    let bytes=System.Text.Encoding.ASCII.GetBytes text;
    let hash=SHA256.Create().ComputeHash(bytes); // Getting SHA256 of the text
    let byteToHex (bytes:byte[]) =
        bytes |> Array.fold (fun state x-> state + sprintf "%02X" x) ""
    let arr=byteToHex hash
    let mutable count = 0;
    for i=0 to arbitrary-1 do 
        if arr.[i]='0' then count<-count+1
    if count=arbitrary then flag<-true // check if No of 0s is true
    if flag = true then 
        printfn "Server : String- %s    SHA256- %s" (text|>string) (arr |>string)
        if start <> 1L then
            ref <! arr // send it to the client 

let LeadingZeros (mailbox:Actor<_>)= //Actor Creation
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | WorkerMsg(start,endI,k) ->    for i in start .. workers .. endI do //Worker 
                                            checkLeadingZeros start k 
                                        mailbox.Sender() <! EndMsg(start)
        | _ -> printfn "Worker Received Wrong message"
    }
    loop()

let workersList=[for a in 1L .. workers do yield(spawn system ("Job" + (string a)) LeadingZeros)]

let Dispatcher (mailbox:Actor<_>) = //Dispatcher
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | DispatcherMsg(startN,endN,k) ->   if startN <> 1L then
                                                ref <! "Hello," + (startN-1L |> string) + ","+ (k |> string)// sending dispatcher command to client 
                                            printfn "Running Dispatcher :"
                                            for i in 0L .. (workers-1L) do
                                                workersList.Item(i|>int) <! WorkerMsg( startN, endN , k )
                                                                
        | EndMsg(index) ->  count <- count+1L
                            if count = workers then
                                if index <> 1L then
                                    ref <! "ProcessingDone"
                                printfn "Processing done on Server"     
                                localWorkDone <- true
        | _ -> printfn "Dispatcher Received Wrong message"
        return! loop()
    }
    loop()

let localDispatcherRef = spawn system "localDisp" Dispatcher // Calling Dispatcher

let commlink = 
    spawn system "server"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! msg = mailbox.Receive()
                printfn "%s" msg 
                client<-true
                let command = (msg|>string).Split ','
                if command.[0].CompareTo("Job")=0 then
                    let n = N/(int64(command.[1])+workers)  * (int64(command.[1]))  |> int64
                    localDispatcherRef <! DispatcherMsg (n+1L|>int64,N|>int64,k|>int64)
                    ref <- mailbox.Sender()
                elif msg.CompareTo ("PD")=0 then 
                    printfn "Processed by client"
                    remoteWorkDone <- true
                elif command.[0].CompareTo("init")=0 then
                    localDispatcherRef <! DispatcherMsg (1L|>int64,N|>int64,k|>int64)
                //else 
                    // printfn "-%s-" msg

                return! loop() 
            }
        loop()

Thread.Sleep(10000)
if client = false then  // if no client connects then run by itself 
    printfn " Client not found, running on server :"
    commlink <! "init"
    remoteWorkDone <- true

while (not localWorkDone && not remoteWorkDone) do
    system.Terminate()
//system.WhenTerminated.Wait()
//Thread.Sleep (10000)

//system.Terminate()