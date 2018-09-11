module ServerCode.Storage.AzureBlob

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open ServerCode
open ServerCode.Domain
open System
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive
open Newtonsoft.Json
open Microsoft.Extensions.Logging
open Fable.AST.Fable

type AzureConnection =
    | AzureConnection of string


let saveAnnotationsToDB connectionString (annotations: ArticleAnnotations) = task {
    ()
}

let getArticlesFromDB connectionString userName = task {
    return { 
      UserName = userName
      Articles = [ for i in 1..5 -> { Title = "DB Article " + string i; ID = string i; Text = None}, false ]              
    }
}



let loadArticleFromDB connectionString link = task {
    return { 
        Title = "Some title"; Text = None; ID = link }
}


let loadArticleAnnotationsFromDB connectionString articleId userName  = task {
    return None
}


module private StateManagement =
    let getStateBlob (AzureConnection connectionString) name = task {
        let client = (CloudStorageAccount.Parse connectionString).CreateCloudBlobClient()
        let state = client.GetContainerReference "state"
        let! _ = state.CreateIfNotExistsAsync()
        return state.GetBlockBlobReference name }

    let resetTimeBlob connectionString = getStateBlob connectionString "resetTime"

    let storeResetTime connectionString = task {
        let! blob = resetTimeBlob connectionString
        return! blob.UploadTextAsync "" }

let getLastResetTime connectionString = task {
    let! blob = StateManagement.resetTimeBlob connectionString
    do! blob.FetchAttributesAsync()
    return blob.Properties.LastModified |> Option.ofNullable |> Option.map (fun d -> d.UtcDateTime)
}
