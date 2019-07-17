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
      Writer: string
      Pictures: string list option}

type Picture =
    { Id: string
      Name: string
      Owner: string
      Tagged: string list option}
    
type Person =
    { Id: string
      FirstName : string
      LastName : string
      Friends: string list option
      Messages: string list option}

let people = [ 
    { Id = "100"; FirstName = "Jane"; LastName = "Milton"; Friends = None; Messages = Some ["1"] }
    { Id = "101"; FirstName = "Travis"; LastName = "Smith"; Friends = Some ["100"]; Messages = Some ["2"] } ]

let messages = [
    { Id = "1"; Message = "A message"; Time = DateTime(2018,12,12); Writer = "100"; Pictures = Some ["1001"]}
    { Id = "2"; Message = "A message 2"; Time = DateTime(2018,12,12); Writer = "101"; Pictures = None}]

let pictures = [
    { Id = "1001"; Name = "A picture"; Owner = "100"; Tagged = Some ["100"; "101"]}]

let getPerson id = people |> List.tryFind (fun p -> p.Id = id)

let getMessage id = messages |> List.tryFind (fun m -> m.Id = id)

let getPicture id = pictures |> List.tryFind (fun  p -> p.Id = id)

let locatePeople friends =
    match friends with
    | Some x -> x |> List.map getPerson |> List.toSeq
    | None -> Seq.empty

let locateMessages messageIds =
    match messageIds with
    | Some x -> x |> List.map getMessage |> List.toSeq
    | None -> Seq.empty

let locatePictures pictureIds =
    match pictureIds with
    | Some x -> x |> List.map getPicture |> List.toSeq
    | None -> Seq.empty

// GraphQL schema 
let rec PersonType =
    Define.Object("Person",
        fieldsFn = fun() ->
        [Define.Field("id", String, fun ctx p -> p.Id)
         Define.Field("firstName", String, fun ctx p -> p.FirstName)
         Define.Field("lastName", String, fun ctx p -> p.LastName)  
         Define.Field("friends", ListOf (Nullable PersonType), fun _ p -> locatePeople p.Friends)
         Define.Field("message", ListOf (Nullable MessageType), fun _ p -> locateMessages p.Messages)])
and MessageType =
    Define.Object<Message>(
        name = "Message",
        fieldsFn = fun ()  ->
                  [Define.Field("id", String, fun _ m -> m.Id)
                   Define.Field("message", String, fun _ m -> m.Message)
                   Define.Field("time", String, fun _ m -> m.Time.ToString())
                   Define.Field("writer", Nullable PersonType, fun _ m -> getPerson m.Writer)
                   Define.Field("pictures", ListOf (Nullable PictureType), fun _ m -> locatePictures m.Pictures)])
and PictureType =
    Define.Object<Picture>(
        name = "Picture",
        fieldsFn = fun () ->
                  [Define.Field("id", String, fun _ p -> p.Id)
                   Define.Field("name", String, fun _ p -> p.Name)
                   Define.Field("owner", Nullable PersonType, fun _ p -> getPerson p.Owner)
                   Define.Field("tagged", ListOf (Nullable PersonType), fun _ p -> locatePeople p.Tagged)])

// each schema must define so-called root query
let QueryRoot = Define.Object("Query", [
    Define.Field("people", ListOf PersonType, fun ctx () -> people)
    Define.Field("person", Nullable PersonType, [ Define.Input("id", String)], fun ctx _ -> getPerson (ctx.Arg("id")))
    Define.Field("message", Nullable MessageType, [ Define.Input("id", String)], fun ctx _ -> getMessage (ctx.Arg("id")))
    Define.Field("picture", Nullable PictureType, [ Define.Input("id", String)], fun ctx _ -> getPicture (ctx.Arg("id")))
])

// then initialize everything as part of schema
let schema = Schema(QueryRoot)

let executor = Executor(schema)

// RESOLUTION
let removeWhitespacesAndLineBreaks (data : string) = data.Trim().Replace("\r\n", " ") 

let getQuery (rawForm : byte[]) =
    let body = System.Text.Encoding.UTF8.GetString(rawForm) |> removeWhitespacesAndLineBreaks
    let value = body |> JToken.Parse
    value.Value<string>("query") |> Some
    
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
