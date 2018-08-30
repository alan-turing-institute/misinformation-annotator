module ServerCode.Storage.AzureTable

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open ServerCode.Domain
open System
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive

type AzureConnection =
    | AzureConnection of string

let getArticlesTable (AzureConnection connectionString) = task {
    let client = (CloudStorageAccount.Parse connectionString).CreateCloudTableClient()
    let table = client.GetTableReference "article"

    // Azure will temporarily lock table names after deleting and can take some time before the table name is made available again.
    let rec createTableSafe() = task {
        try
        let! _ = table.CreateIfNotExistsAsync()
        ()
        with _ ->
            do! Task.Delay 5000
            return! createTableSafe() }

    do! createTableSafe()
    return table }

/// Load from the database
let getAnnotationsFromDB connectionString userName = task {
    let! results = task {
        let! table = getArticlesTable connectionString
        let query = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, userName)
        return! table.ExecuteQuerySegmentedAsync(TableQuery(FilterString = query), null)  }
    return
        { UserName = userName
          Articles =
            [ for result in results ->
                { Title = result.Properties.["Title"].StringValue
                  ID = string result.Properties.["Link"].StringValue 
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