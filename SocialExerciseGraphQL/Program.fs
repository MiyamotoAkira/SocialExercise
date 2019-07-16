// Learn more about F# at http://fsharp.org

open System
open System.Threading
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open FSharp.Data.GraphQL
open FSharp.Data.GraphQL.Types
open FSharp.Data.GraphQL.Execution
open Newtonsoft.Json
open Newtonsoft.Json.Linq

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

let removeWhitespacesAndLineBreaks (data : string) =
    data.Trim().Replace("\r\n", " ") 

let getQuery (rawForm : byte[]) =
    let body = System.Text.Encoding.UTF8.GetString(rawForm) |> removeWhitespacesAndLineBreaks
    printfn "%s" body
    match body.Contains("people") with
    | true -> let value = body |> JToken.Parse
              value.Value<string>("query") |> Some
    | _ -> None
    
let serialize =
    function
    | Direct (data, _) -> JsonConvert.SerializeObject(data)
    | _ -> ""

let executeSchemaQuery (query : string option) =
    match query with
    | Some x -> x  |> executor.AsyncExecute |> Async.RunSynchronously
    | None -> Introspection.IntrospectionQuery |> executor.AsyncExecute |> Async.RunSynchronously

let app =
    choose
      [ GET >=> path "/" >=> OK "Hello Get"
        POST >=> choose
            [ path "/graphql" >=> request ( fun r-> OK (r.rawForm |> getQuery |> executeSchemaQuery |> serialize))
              path "/test" >=> request (fun r-> OK (System.Text.Encoding.UTF8.GetString(r.rawForm) |> removeWhitespacesAndLineBreaks ))]]
        
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
