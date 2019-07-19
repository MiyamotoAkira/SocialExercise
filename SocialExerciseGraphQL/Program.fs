open System
open System.IO
open System.Threading
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Giraffe
open Giraffe.HttpStatusCodeHandlers.Successful
open FSharp.Data.GraphQL
open FSharp.Data.GraphQL.Types
open FSharp.Data.GraphQL.Execution
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open FSharp.Control.Tasks

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

let getQuery body =
    let value = body |> JToken.Parse
    value.Value<string>("query") |> Some
    
let extractResponse =
    function
    | Direct (data, _) -> data
    | _ -> null

let executeSchemaQuery  =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()
            let query = getQuery body
            let! result = 
              match query with
              | Some x -> x  |> executor.AsyncExecute
              | None -> Introspection.IntrospectionQuery |> executor.AsyncExecute              
            return! json (extractResponse result) next ctx
        }

let webApp =
    choose
      [ GET >=> route "/" >=> OK "Hello Get"
        POST >=> route "/graphql" >=> executeSchemaQuery]

//Giraffe setup
let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

let configureApp (app : IApplicationBuilder) =
    app.UseGiraffeErrorHandler(errorHandler)
       .UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    services
    |> fun s -> s.AddGiraffe() 
    |> ignore

let configureLogging (builder:ILoggingBuilder) = 
  builder.AddFilter(fun lvl -> lvl = LogLevel.Information)
         .AddConsole()
         |> ignore

[<EntryPoint>]
let main argv =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    
    WebHost.CreateDefaultBuilder(argv)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(Action<IServiceCollection> configureServices)
        .ConfigureLogging(configureLogging)
        .UseWebRoot(webRoot)
        .Build()
        .Run()
    

    0 // return an integer exit code
