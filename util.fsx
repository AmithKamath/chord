open System
open System.Security.Cryptography

let get2Pow (n : int) : bigint =
    let multipler = bigint(2)
    let mutable res = bigint(1)
    for i in 1..n do
        res <- res * multipler
    res

let getDecimal (bytarr: byte[]) : bigint =
    let mutable multiplier = bigint(1)
    let mutable two = bigint(2)
    let mutable res = bigint(0)
    for i in bytarr.Length - 1..-1..0 do
        let mutable byt = int(bytarr.[i])
        for j in 1..8 do
            let bit = bigint(byt &&& 1)
            res <- res + (bit * multiplier)
            multiplier <- multiplier * two
            byt <- byt >>> 1
    res

let getIds (num : int) : bigint[] =
    let random = new Random()
    let sha1 = SHA1Managed.Create()

    Array.init num (fun idx -> 
        let b: byte[] = Array.zeroCreate 20
        random.NextBytes(b)
        let shaHash = sha1.ComputeHash(b)
        getDecimal(shaHash)
    )


(*let peers = getIds 5000

for i in 0..peers.Length - 1 do
    printfn "%A" (peers.[i])*)