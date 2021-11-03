#r "nuget: Akka.Remote, 1.4.25"
#r "nuget: Akka, 1.4.25"
#r "nuget: Akka.FSharp, 1.4.25"
#load "util.fsx"

open Akka.FSharp
open Akka.Actor
open System
open System.Threading
open Util

let INVALID_CONST = bigint(-1)
let mutable terminate = false

type PeerMessages = 
    | CreateFirstChordPeer of identifierBits : int * n : bigint
    | CreatePeer of identifierBits : int * n : bigint
    | Join of bigint
    | FindSuccessor of originPeer : bigint * id : bigint * queryType : int * fingerIndex : int * hops : int
    | SuccessorFound of bigint
    | UpdateFingerSuccessor of succ : bigint * idx : int
    | Stabilize
    | SendPredecessor
    | PredecessorReceived of bigint
    | Notify of bigint
    | FixFingers
    | KeyFound of peerId : bigint * hops : int
    | StartStabilization
    | StopStabilization
    | NumOfRequests of int
    | PrintDetails
    | None

type MasterMessages =
    | InitializeMaster of noOfPeers : int * noOfRequests : int
    | UpdateHopsCount of totalHops : int
    | PrintAvgHops

let system = System.create "local-system" <| Configuration.load ()

let master (mailbox: Actor<_>) = 
    let mutable hops = 0
    let mutable numberOfPeers = 0
    let mutable received = 0
    let mutable numberOfRequests = 0

    let rec loop() = actor{
        let! message = mailbox.Receive()
        match message with
        
        | InitializeMaster(noOfPeers, noOfRequests) ->
            numberOfPeers <- noOfPeers
            numberOfRequests <- noOfRequests

        | UpdateHopsCount(totalHops)->
            hops <- hops + totalHops
            received <- received + 1
            if received = numberOfPeers then
                let masterRef = system.ActorSelection("akka://local-system/user/master")
                masterRef <! PrintAvgHops
        
        | PrintAvgHops ->
            let res = double(hops) / double(numberOfPeers * numberOfRequests)
            printfn "The average hop count is %f" res
            terminate <- true
        return! loop()
    }
    loop()

let peer (mailbox: Actor<_>) = 
    let mutable next = 0
    let mutable fingerTable : bigint[] = Array.empty
    let mutable predecessor = bigint(-1)
    let mutable successor = bigint(-1)
    let mutable n = bigint(0)
    let mutable m = bigint(0)
    let mutable identifierLength = 0
    let stabilizationCancellationToken = new CancellationTokenSource()
    let fingerTblUpdtCancellationToken = new CancellationTokenSource()
    let mutable totalNumHops = 0
    let mutable totalRequests = 0  

    let rec loop() = actor{
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()
        match message with

        | CreateFirstChordPeer(identifierBits, num) ->
            n <- num
            m <- get2Pow identifierBits
            identifierLength <- identifierBits
            fingerTable <- [|for i in 1 .. identifierLength -> INVALID_CONST|]
            predecessor <- n
            successor <- n

        | CreatePeer(identifierBits, num) ->
            n <- num
            m <- get2Pow identifierBits
            identifierLength <- identifierBits
            fingerTable <- [|for i in 1 .. identifierLength -> INVALID_CONST|]
            successor <- INVALID_CONST
            predecessor <- INVALID_CONST

        | FindSuccessor(originPeer, id, queryType, fingerIdx, hops) ->
            // id E (n, succ)
            let mutable found = false
            if n >= successor then
                found <- (id > n) || (id <= successor)
            else 
                found <- (id > n) && (id <= successor)

            if found then
                let originPeerRef = system.ActorSelection("akka://local-system/user/" + string(originPeer))
                if queryType = 1 then originPeerRef <! SuccessorFound(successor)
                elif queryType = 2 then originPeerRef <! UpdateFingerSuccessor(successor, fingerIdx)
                elif queryType = 3 then originPeerRef <! KeyFound(successor, hops)
            else
                let mutable executeloop = true
                let mutable i = identifierLength - 1
                let mutable nextSucc = n

                while executeloop do
                    let succ = fingerTable.[i]
                    if succ <> INVALID_CONST then
                        //succ E (n, id)
                        if n >= id then
                            if (succ < n) && (succ < id) then
                                nextSucc <- succ
                                executeloop <- false                                   
                            elif (succ > n) then
                                nextSucc <- succ
                                executeloop <- false            
                        elif (succ > n) && (succ < id) then
                            nextSucc <- succ
                            executeloop <- false          
                    i <- i - 1
                    if i = -1 then executeloop <- false        
                
                let successorPeerRef = system.ActorSelection("akka://local-system/user/" + string(nextSucc))
                successorPeerRef <! FindSuccessor(originPeer, id, queryType, fingerIdx, hops + 1)

        | Join(chordPeer) ->
            let chordPeerRef = system.ActorSelection("akka://local-system/user/" + string(chordPeer))
            chordPeerRef <! FindSuccessor(n, n, 1, 0, 0)
        
        | SuccessorFound(succ) ->
            successor <- succ
            
        | Stabilize ->
            if successor <> INVALID_CONST then
                let successorRef = system.ActorSelection("akka://local-system/user/" + string(successor))
                successorRef <! SendPredecessor

        | PredecessorReceived(pred) ->
            let mutable successorChanged = false
            //pred E (n, succ)
            if n >= successor then
                successorChanged <- (pred > n) || (pred < successor)
            else
                successorChanged <- (pred > n) && (pred < successor)

            if successorChanged then
                successor <- pred

            let successorPeerRef = system.ActorSelection("akka://local-system/user/" + string(successor))
            successorPeerRef <! Notify(n)

        | SendPredecessor ->
            if predecessor <> INVALID_CONST then
                sender.Tell(PredecessorReceived(predecessor))

        | Notify(peer) ->
            let mutable changePred = false
            if predecessor = n then
               changePred <- true
            elif predecessor = INVALID_CONST then 
                changePred <- true
            else
                // peer E (predecessor, n)
                if predecessor >= n then
                    changePred <- (peer > predecessor) || (peer < n)
                else
                    changePred <- (peer > predecessor) && (peer < n)
            if changePred then predecessor <- peer

        | FixFingers ->
            if successor <> INVALID_CONST then
                next <- next + 1
                if next > identifierLength then
                    next <- 1

                let mutable value = get2Pow (next - 1)
                value <- (n + value) % m

                let path = "akka://local-system/user/" + string(n)
                let currentPeerRef = system.ActorSelection(path)
                currentPeerRef <! FindSuccessor(n, value, 2, next, 0)

        | UpdateFingerSuccessor(succ, fingeridx) ->
            fingerTable.[fingeridx - 1] <- succ

        | StartStabilization ->
            let stabilize = async{
                while true do
                    let peerActorRef = system.ActorSelection("akka://local-system/user/" + string(n))
                    peerActorRef <! Stabilize
                    peerActorRef <! FixFingers
                    do! Async.Sleep 1000
            }

            Async.Start(stabilize, stabilizationCancellationToken.Token)

        | StopStabilization ->
            stabilizationCancellationToken.Cancel()
           
        | PrintDetails ->
            printfn "n -  %A" n
            printfn "successor - %A" successor
            printfn "predecessor - %A" predecessor
            for i = 0 to identifierLength - 1 do
                printf "%A " fingerTable.[i]
            printfn " "

        | KeyFound(peerId, hops) ->
            //printfn "Key found at %A with total number of hops : %d" peerId hops
            totalNumHops <- totalNumHops + hops
            totalRequests <- totalRequests - 1
            if totalRequests = 0 then
                let masterRef = system.ActorSelection("akka://local-system/user/master")
                masterRef <! UpdateHopsCount(totalNumHops)
        
        | NumOfRequests(numOfRequests) ->
            totalRequests <- numOfRequests
        | None ->
            printfn "I dont understand the message"

        return! loop()
    }
    loop()


let numOfReq = int(fsi.CommandLineArgs.[2])
let numOfPeers = int(fsi.CommandLineArgs.[1])

let masterRef =  spawn system "master" <| master 
masterRef <! InitializeMaster(numOfPeers, numOfReq)

let mutable peers = []
let stabCancellationToken = new CancellationTokenSource()
let updtFingerCancellationToken = new CancellationTokenSource()


let stabilize = async{
   
    while true do
        for peer in peers do
            
            let peerActorRef = system.ActorSelection("akka://local-system/user/" + peer)
            peerActorRef <! Stabilize
        do! Async.Sleep 50
}

let updtFinger = async{
   
    while true do
        for peer in peers do
            
            let peerActorRef = system.ActorSelection("akka://local-system/user/" + peer)
            peerActorRef <! FixFingers
        do! Async.Sleep 50
}

Async.Start(stabilize, stabCancellationToken.Token)
Async.Start(updtFinger, updtFingerCancellationToken.Token)

let mutable firstChordPeer = INVALID_CONST

let addPeer (peerId : bigint) (firstPeer:bool) = 
    let newPeer = spawn system (string(peerId)) <| peer
    peers <- List.append peers [string(peerId)]
    if firstPeer then
        firstChordPeer <- peerId
        newPeer <! CreateFirstChordPeer(160, peerId)     
    else
        newPeer <! CreatePeer(160, peerId)
        newPeer <! Join(firstChordPeer)
    //newPeer <! StartStabilization
    newPeer <! NumOfRequests(numOfReq)

let stopStabilization = 
    for i in 0 .. peers.Length - 1 do
        let peerRef = system.ActorSelection("akka://local-system/user/" + peers.[i])
        peerRef <! StopStabilization

let printPeerDetails peerId = 
    let peerRef = system.ActorSelection("akka://local-system/user/" + string(peerId))
    peerRef <! PrintDetails
    System.Console.ReadLine() |> ignore

let findKey peerId key  = 
    let peerRef = system.ActorSelection("akka://local-system/user/" + string(peerId))
    peerRef <! FindSuccessor(peerId, key, 3, 0, 0)


let newPeers = getIds numOfPeers
//let newPeers : bigint[] = [|bigint(10); bigint(100); bigint(200); bigint(300); bigint(400); bigint(500); bigint(600); bigint(700);|]

addPeer newPeers.[0] true

for x in 1..newPeers.Length - 1 do
    addPeer newPeers.[x] false 

printfn "Wait for stabilization (depending on the number of nodes). After waiting, Please press a key to continue"
System.Console.ReadLine() |> ignore

(*for x in 0..newPeers.Length - 1 do
    printPeerDetails newPeers.[x]*)

let requests = getIds numOfReq
//let requests : bigint[] = [|bigint(369)|]

for x in 0..newPeers.Length - 1 do
    let peerRef = system.ActorSelection("akka://local-system/user/" + string(newPeers.[x]))
    for y in 0..requests.Length - 1 do
        peerRef <! FindSuccessor(newPeers.[x], requests.[y], 3, 0, 0)

(*System.Console.ReadLine() |> ignore

let peer1ref = system.ActorSelection("akka://local-system/user/10")
peer1ref <! FindSuccessor(bigint(10), bigint(269), 3, 0, 0)

System.Console.ReadLine() |> ignore
printfn "Adding 269"

addPeer (bigint(269)) false
printfn "Wait for stabilization"

System.Console.ReadLine() |> ignore

let peer1refagain = system.ActorSelection("akka://local-system/user/100")
peer1refagain <! FindSuccessor(bigint(100), bigint(260), 3, 0, 0)*)
let mutable obsoleteOp = true
while not terminate do
    obsoleteOp <- true
//System.Console.ReadLine() |> ignore
stabCancellationToken.Cancel()
updtFingerCancellationToken.Cancel()