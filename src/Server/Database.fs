/// Functions for managing the database.
module ServerCode.Database

open Microsoft.Azure.WebJobs
open ServerCode.Storage.AzureStorage
open ServerCode
open ServerCode.Domain
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive

[<RequireQualifiedAccess>]
type DatabaseType =
    | FileSystem
    | AzureStorage of connectionString : AzureConnection

type IDatabaseFunctions =
    abstract member LoadArticles : string -> Task<Domain.ArticleList>
    abstract member SaveAnnotations : Domain.ArticleAnnotations -> Task<unit>
    abstract member DeleteAnnotations : Domain.ArticleAnnotations -> Task<bool>
    abstract member GetLastResetTime : unit -> Task<System.DateTime>
    abstract member LoadArticle : string -> Task<Domain.Article>
    abstract member LoadArticleAnnotations : string -> string -> Task<Domain.ArticleAnnotations option>
    abstract member IsValidUser : string -> string -> Task<UserData option>
    abstract member FlagArticle : Domain.FlaggedArticle -> Task<bool>

/// Start the web server and connect to database
let getDatabase databaseType startupTime =
    match databaseType with
    | DatabaseType.AzureStorage connection ->
        //Storage.WebJobs.startWebJobs connection
        { new IDatabaseFunctions with
            member __.LoadArticles key = Storage.AzureStorage.getArticlesFromDB connection key
            member __.LoadArticle key = Storage.AzureStorage.loadArticleFromDB connection key
            member __.SaveAnnotations annotations = Storage.AzureStorage.saveAnnotationsToDB connection annotations
            member __.DeleteAnnotations annotations = Storage.AzureStorage.deleteAnnotationsFromDB connection annotations 
            member __.GetLastResetTime () = task {
                let! resetTime = Storage.AzureStorage.getLastResetTime connection
                return resetTime |> Option.defaultValue startupTime } 
            member __.LoadArticleAnnotations articleId userName = Storage.AzureStorage.loadArticleAnnotationsFromDB connection articleId userName
            member __.IsValidUser userName password = Storage.AzureStorage.IsValidUser connection userName password
            member __.FlagArticle flaggedArticle = Storage.AzureStorage.FlagArticle connection flaggedArticle 
        }

    | DatabaseType.FileSystem ->
        { new IDatabaseFunctions with
            member __.LoadArticles key = task { return Storage.FileSystem.getArticlesFromDB key }
            member __.SaveAnnotations annotations = task { return Storage.FileSystem.saveAnnotationsToDB annotations }
            member __.DeleteAnnotations annotations = task { return Storage.FileSystem.deleteAnnotationsFromDB annotations }
            member __.GetLastResetTime () = task { return startupTime } 
            member __.LoadArticle key = task { return Storage.FileSystem.loadArticleFromDB key }
            member __.LoadArticleAnnotations articleId userName = task { return Storage.FileSystem.loadArticleAnnotationsFromDB articleId userName }
            member __.IsValidUser userName password = task { return Storage.FileSystem.IsValidUser userName password } 
            member __.FlagArticle flaggedArticle = task { return true }
        }
            
