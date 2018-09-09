/// Functions for managing the database.
module ServerCode.Database

open Microsoft.Azure.WebJobs
open ServerCode.Storage.AzureBlob
open ServerCode
open ServerCode.Domain
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive

[<RequireQualifiedAccess>]
type DatabaseType =
    | FileSystem
    | AzureStorage of connectionString : AzureConnection

type IDatabaseFunctions =
    abstract member LoadAnnotations : string -> Task<Domain.ArticleList>
    abstract member SaveAnnotations : Domain.ArticleAnnotations -> Task<unit>
    abstract member GetLastResetTime : unit -> Task<System.DateTime>
    abstract member LoadArticle : string -> Task<Domain.Article>
    abstract member LoadArticleAnnotations : string -> string -> Task<Domain.ArticleAnnotations option>

/// Start the web server and connect to database
let getDatabase databaseType startupTime =
    match databaseType with
    | DatabaseType.AzureStorage connection ->
        //Storage.WebJobs.startWebJobs connection
        { new IDatabaseFunctions with
            member __.LoadAnnotations key = Storage.AzureBlob.getAnnotationsFromDB connection key
            member __.LoadArticle key = Storage.AzureBlob.loadArticleFromDB connection key
            member __.SaveAnnotations annotations = Storage.AzureBlob.saveAnnotationsToDB connection annotations
            member __.GetLastResetTime () = task {
                let! resetTime = Storage.AzureBlob.getLastResetTime connection
                return resetTime |> Option.defaultValue startupTime } 
            member __.LoadArticleAnnotations articleId userName = Storage.AzureBlob.loadArticleAnnotationsFromDB articleId userName
        }

    | DatabaseType.FileSystem ->
        { new IDatabaseFunctions with
            member __.LoadAnnotations key = task { return Storage.FileSystem.getArticlesFromDB key }
            member __.SaveAnnotations annotations = task { return Storage.FileSystem.saveAnnotationsToDB annotations }
            member __.GetLastResetTime () = task { return startupTime } 
            member __.LoadArticle key = task { return Storage.FileSystem.loadArticleFromDB key }
            member __.LoadArticleAnnotations articleId userName = task { return Storage.FileSystem.loadArticleAnnotationsFromDB articleId userName } }
            
