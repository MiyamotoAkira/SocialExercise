// Learn more about F# at http://fsharp.org

open System
open System.Threading
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open FSharp.Data.GraphQL
open FSharp.Data.GraphQL.Types

type Message =
    { message: string
      time: DateTime}

type Person =
    { FirstName : string;
      LastName : string}

let people = [ 
    { FirstName = "Jane"; LastName = "Milton" }
    { FirstName = "Travis"; LastName = "Smith" } ]

// GraphQL type definition for Person type
let Person = Define.Object("Person", [
    Define.Field("firstName", String, fun ctx p -> p.FirstName)
    Define.Field("lastName", String, fun ctx p -> p.LastName)  
])

// each schema must define so-called root query
let QueryRoot = Define.Object("Query", [
    Define.Field("people", ListOf Person, fun ctx () -> people)
])

// then initialize everything as part of schema
let schema = Schema(QueryRoot)

let executor = Executor(schema)

let getString (rawForm : byte[]) =
    System.Text.Encoding.UTF8.GetString(rawForm)

let app =
    choose
      [ GET >=> path "/" >=> OK "Hello Get"
        POST >=> path "/graphql" >=> request( fun r-> OK (r.rawForm |> getString))]
        
[<EntryPoint>]
let main argv =
    let cts = new CancellationTokenSource()
    let conf = { defaultConfig with cancellationToken = cts.Token }
    let listening, server = startWebServerAsync conf app 
    
    Async.Start(server, cts.Token)
    printfn "Make requests now"
    Console.ReadKey true |> ignore
    
    cts.Cancel()

    0 // return an integer exit code
