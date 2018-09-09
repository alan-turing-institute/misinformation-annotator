module ServerCode.Storage.AzureBlob

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open ServerCode.Domain
open System
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive
open Newtonsoft.Json

type AzureConnection =
    | AzureConnection of string

let getArticlesBlob (AzureConnection connectionString) = task {
    let blobClient = (CloudStorageAccount.Parse connectionString).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("sample-crawl")
    let articleBlob = container.GetBlockBlobReference "articles/misinformation.txt"
    return articleBlob }

/// Load list of articles from the database
let getArticlesFromDB connectionString userName = task {
    let! results = task {
        let! articleBlob = getArticlesBlob connectionString
        // TODO: Find articles that should be displayed to the specific user
        return! articleBlob.DownloadTextAsync()
    }
    let articles = 
        results.Split '\n'
        |> Array.map (fun articleLine ->
                articleLine |> JsonConvert.DeserializeObject<ArticleDBData>
            )

    let getTitle (a:ArticleDBData) =
        a.MicroformatMetadata.OpenGraph 
        |> Array.collect (fun og -> 
            og.Properties 
            |> Array.filter (fun p -> p |> Array.contains "og:title" )
            |> Array.map (fun a' -> a'.[1]))
        |> fun arr -> if arr.Length > 0 then arr.[0] else "Unknown title"
        
    return
        { UserName = userName
          Articles =
            [ for article in articles ->
                { Title = getTitle article
                  ID = article.ArticleUrl
                  Text = None}, false ] } }

/// load article from the database
let loadArticleFromDB connectionString article = task {
   return
        { Title = ""; ID = ""; Text = None }
    }

let loadArticleAnnotationsFromDB articleId userName = task {
    return 
        None
    }


/// Save to the database
let saveAnnotationsToDB connectionString (annotations: ArticleAnnotations) = task {
    (*
    let buildEntity userName (article : Article) =
        let isAllowed = string >> @"/\#?".Contains >> not
        let entity = DynamicTableEntity()
        entity.PartitionKey <- userName
        entity.RowKey <- article.Title.ToCharArray() |> Array.filter isAllowed |> String
        entity

    let! existingAnnotations = getAnnotationsFromDB connectionString annotations.UserName
    let batch =
        let operation = TableBatchOperation()
        let existingArticles = existingAnnotations.Articles |> Set
        let newArticles = annotations.Articles |> Set

        // Delete obsolete articles
        (existingArticles - newArticles)
        |> Set.iter(fun article ->
            let entity = buildEntity annotations.UserName article
            entity.ETag <- "*"
            entity |> TableOperation.Delete |> operation.Add)

        // Insert new / update existing articles
        (newArticles - existingArticles)
        |> Set.iter(fun article ->
            let entity = buildEntity annotations.UserName article
            entity.Properties.["Title"] <- EntityProperty.GeneratePropertyForString article.Title
            entity.Properties.["Link"] <- EntityProperty.GeneratePropertyForString article.ID
            entity.Properties.["Text"] <- EntityProperty.GeneratePropertyForString ""
            entity |> TableOperation.InsertOrReplace |> operation.Add)

        operation

    let! articlesTable = getArticlesTable connectionString
    let! _ = articlesTable.ExecuteBatchAsync batch
    return () 
    *)
    return ()
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



/// Clears all Annotationss and records the time that it occurred at.
// let clearAnnotationss connectionString = task {
//     let! table = getArticlesTable connectionString
//     let! _ = table.DeleteIfExistsAsync()

//     let! _ = Defaults.defaultAnnotations "test" |> saveAnnotationsToDB connectionString
//     do! StateManagement.storeResetTime connectionString }