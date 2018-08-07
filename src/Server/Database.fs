/// Functions for managing the database.
module ServerCode.Database

open Microsoft.Azure.WebJobs
open ServerCode.Storage.AzureTable
open ServerCode
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive

[<RequireQualifiedAccess>]
type DatabaseType =
    | FileSystem
    | AzureStorage of connectionString : AzureConnection

type IDatabaseFunctions =
    abstract member LoadAnnotations : string -> Task<Domain.Annotations>
    abstract member SaveAnnotations : Domain.Annotations -> Task<unit>
    abstract member GetLastResetTime : unit -> Task<System.DateTime>
    abstract member LoadArticle : Domain.Article -> Task<Domain.Article>

/// Start the web server and connect to database
let getDatabase databaseType startupTime =
    match databaseType with
    | DatabaseType.AzureStorage connection ->
        //Storage.WebJobs.startWebJobs connection
        { new IDatabaseFunctions with
            member __.LoadAnnotations key = Storage.AzureTable.getAnnotationsFromDB connection key
            member __.LoadArticle key = Storage.AzureTable.loadArticleFromDB connection key
            member __.SaveAnnotations annotations = Storage.AzureTable.saveAnnotationsToDB connection annotations
            member __.GetLastResetTime () = task {
                let! resetTime = Storage.AzureTable.getLastResetTime connection
                return resetTime |> Option.defaultValue startupTime } }

    | DatabaseType.FileSystem ->
        { new IDatabaseFunctions with
            member __.LoadAnnotations key = task { return Storage.FileSystem.getArticlesFromDB key }
            member __.SaveAnnotations annotations = task { return Storage.FileSystem.saveAnnotationsToDB annotations }
            member __.GetLastResetTime () = task { return startupTime } 
            member __.LoadArticle key = task { return Storage.FileSystem.loadArticleFromDB key }}

