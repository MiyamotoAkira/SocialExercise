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

// SCHEMA
type Message =
    { Id: string
      Message: string
      Time: DateTime
      Writer: string}

type Pictures =
    { Id: string
      Name: string
      Owner: string
      Tagged: string list option}
    
type Person =
    { Id: string
      FirstName : string
      LastName : string
      Friends: string list option}

let people = [ 
    { Id = "100"; FirstName = "Jane"; LastName = "Milton"; Friends = None }
    { Id = "101"; FirstName = "Travis"; LastName = "Smith"; Friends = Some ["100"] } ]

let messages = [
    { Id = "1"; Message = "A message"; Time = DateTime(2018,12,12); Writer = "100"}
    { Id = "2"; Message = "A message 2"; Time = DateTime(2018,12,12); Writer = "101"}
    ]

let pictures = [
    { Id = "1001"; Name = "A picture"; Owner = "100"; Tagged = Some ["100"; "101"]}
    ]

let getPerson id = people |> List.tryFind (fun p -> p.Id = id)

let getMessage id = messages |> List.tryFind (fun m -> m.Id = id)

let getPicture id = pictures |> List.tryFind (fun  p -> p.Id = id)

let extractFriends person =
    match person.Friends with
    | Some x -> x |> List.map getPerson |> List.toSeq
    | None -> Seq.empty

// GraphQL type definition for Person type
let rec PersonType =
    Define.Object("Person",
        fieldsFn = fun() ->
        [
            Define.Field("id", String, fun ctx p -> p.Id)
            Define.Field("firstName", String, fun ctx p -> p.FirstName)
            Define.Field("lastName", String, fun ctx p -> p.LastName)  
            Define.Field("friends", ListOf (Nullable PersonType), fun _ p -> extractFriends p)
])



// each schema must define so-called root query
let QueryRoot = Define.Object("Query", [
    Define.Field("people", ListOf PersonType, fun ctx () -> people)
])

// then initialize everything as part of schema
let schema = Schema(QueryRoot)

let executor = Executor(schema)

// RESOLUTION
let removeWhitespacesAndLineBreaks (data : string) = data.Trim().Replace("\r\n", " ") 

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
        POST >=> path "/graphql" >=> request ( fun r-> OK (r.rawForm |> getQuery |> executeSchemaQuery |> serialize))]
        
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
