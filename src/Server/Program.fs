﻿/// Server program entry point module.
module ServerCode.Program

open System
open System.IO
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Newtonsoft.Json
open Giraffe
open Giraffe.Serialization.Json
open Giraffe.HttpStatusCodeHandlers.ServerErrors

let GetEnvVar var =
    match Environment.GetEnvironmentVariable(var) with
    | null -> None
    | value -> Some value

let getPortsOrDefault defaultVal =
    match Environment.GetEnvironmentVariable("SUAVE_FABLE_PORT") with
    | null -> defaultVal
    | value -> value |> uint16

let errorHandler (ex : Exception) (logger : ILogger) =
    match ex with
    | :? Microsoft.WindowsAzure.Storage.StorageException as dbEx ->
        let msg = sprintf "An unhandled Windows Azure Storage exception has occured: %s" dbEx.Message
        logger.LogError (EventId(), dbEx, "An error has occured when hitting the database.")
        SERVICE_UNAVAILABLE msg
    | _ ->
        logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
        clearResponse >=> INTERNAL_ERROR ex.Message

let configureApp db root (app : IApplicationBuilder) =
    app.UseGiraffeErrorHandler(errorHandler)
       .UseStaticFiles()
       .UseGiraffe (WebServer.webApp db root)

let configureServices (services : IServiceCollection) =
    // Add default Giraffe dependencies
    services.AddGiraffe() |> ignore

    // Configure JsonSerializer to use Fable.JsonConverter
    let fableJsonSettings = JsonSerializerSettings()
    fableJsonSettings.Converters.Add(Fable.JsonConverter())

    services.AddSingleton<IJsonSerializer>(
        NewtonsoftJsonSerializer(fableJsonSettings)) |> ignore

let configureLogging (loggerBuilder : ILoggingBuilder) =
    loggerBuilder.AddFilter(fun lvl -> lvl.Equals LogLevel.Error)
                 .AddConsole()
                 .AddDebug() |> ignore

[<EntryPoint>]
let main args =
    try
        let args = Array.toList args
        let clientPath =
            match args with
            | clientPath:: _  when Directory.Exists clientPath -> clientPath
            | _ ->
                // did we start from server folder?
                let devPath = Path.Combine("..","Client")
                if Directory.Exists devPath then devPath
                else
                    // maybe we are in root of project?
                    let devPath = Path.Combine("src","Client")
                    if Directory.Exists devPath then devPath
                    else @"./client"
            |> Path.GetFullPath
      
        let storageEnvVar = "CUSTOMCONNSTR_BLOBSTORAGE"
        let connStr1 = GetEnvVar storageEnvVar
        let sqlserverEnvVar = "SQLAZURECONNSTR_SQLDATABASE"
        let connStr2 = GetEnvVar sqlserverEnvVar
        
        let database = 
            match connStr1, connStr2 with
            | Some cs1, Some cs2 ->
                {ServerCode.Storage.AzureStorage.BlobConnection = cs1; 
                 ServerCode.Storage.AzureStorage.SqlConnection = cs2}
                |> Database.DatabaseType.AzureStorage
            | _ -> Database.DatabaseType.FileSystem

        let port = getPortsOrDefault 8085us

        WebHost
            .CreateDefaultBuilder()
            .UseWebRoot(clientPath)
            .UseContentRoot(clientPath)
            .ConfigureLogging(configureLogging)
            .ConfigureServices(configureServices)
            .Configure(Action<IApplicationBuilder> (configureApp database clientPath))
            .UseUrls("http://0.0.0.0:" + port.ToString() + "/")
            .Build()
            .Run()
        0
    with
    | exn ->
        let color = Console.ForegroundColor
        Console.ForegroundColor <- System.ConsoleColor.Red
        Console.WriteLine(exn.Message)
        Console.ForegroundColor <- color
        1
