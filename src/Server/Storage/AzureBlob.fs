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
open System.Text.RegularExpressions
open System.Data.SqlClient


type AzureConnection = {
    BlobConnection : string
    SqlConnection : string 
}

type DBArticle = {
    Id: int
    SiteName: string
    CrawlDate: System.DateTimeOffset
    ArticleUrl: string
    Content: string
    Metadata: string
}

let selectNumArticlesPerSite numArticlesPerSite sqlConn = 
  use conn = new System.Data.SqlClient.SqlConnection(sqlConn)
  conn.Open()
  let command = sprintf "WITH sorted_articles_by_site AS (
    SELECT *, ROW_NUMBER() 
    over (
        PARTITION BY [site_name] 
        order by [id]
    ) AS row_num 
    FROM [articles]
)
SELECT * FROM sorted_articles_by_site WHERE row_num <= %i" numArticlesPerSite

  use cmd = new SqlCommand(command, conn)
  let rdr = cmd.ExecuteReader()
  [| while rdr.Read() do 
        yield { Id = rdr.GetInt32(0)
                SiteName = rdr.GetString(1)
                CrawlDate = rdr.GetDateTimeOffset(2)
                ArticleUrl = rdr.GetString(3)
                Content = rdr.GetString(4)
                Metadata = rdr.GetString(5)} |]

let getJSONFileName userName (articleId: string) = 
    let id = articleId.Replace("/", "-")
    sprintf "%s/%s.json" userName id

let getAnnotationsBlob (connectionString : AzureConnection) userName articleId = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("sample-crawl")
    let annotationBlob = container.GetBlockBlobReference ("annotations/" + (getJSONFileName userName articleId))
    return annotationBlob }

let getExistingAnnotationBlob ( connectionString : AzureConnection) userName articleId = task {
    let! annotationBlob = getAnnotationsBlob (connectionString) userName articleId
    let! exists = annotationBlob.ExistsAsync()
    if exists then 
        return Some annotationBlob
    else
        return None
}

let checkAnnotationsExist (connectionString : AzureConnection) userName (articles : ArticleDBData array) = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("sample-crawl")

    let annotations = 
        articles 
        |> Array.map (fun article ->
            let annotationBlob = container.GetBlockBlobReference ("annotations/" + (getJSONFileName userName article.article_url))
            article, annotationBlob.ExistsAsync().Result)

    return annotations
}
    
let getArticlesBlob (connectionString : AzureConnection) = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("sample-crawl")
    let articleBlob = container.GetBlockBlobReference "articles/misinformation.txt"
    return articleBlob }

let getTitle (a:ArticleDBData) =
    a.microformat_metadata.opengraph 
    |> Array.collect (fun og -> 
        og.properties 
        |> Array.filter (fun p -> p |> Array.contains "og:title" )
        |> Array.map (fun a' -> a'.[1]))
    |> fun arr -> if arr.Length > 0 then arr.[0] else "Unknown title"

let loadArticlesFromFile connectionString = task {
    let! results = task {
        let! articleBlob = getArticlesBlob connectionString
        // TODO: Find articles that should be displayed to the specific user
        return! articleBlob.DownloadTextAsync()
    }
    
    let articles = 
        results.Split '\n'
        |> Array.filter (fun a -> a <> "")
        |> Array.map (fun articleLine ->
                articleLine |> JsonConvert.DeserializeObject<ArticleDBData>
            )
    return articles        
}

let loadArticlesFromSQLDatabase connectionString = task {
    let! results = task {
        // TODO: Find articles that should be displayed to the specific user
        let articles = selectNumArticlesPerSite 10 connectionString.SqlConnection
        return articles
    }
    
    System.IO.File.WriteAllText("/Users/egabasova/Projects/misinformation-annotator/metadata.json", results.[0].Metadata)
    let articles = 
        results
        |> Array.map (fun articleData ->
            {
                site_name = articleData.SiteName
                article_url = articleData.ArticleUrl
                microformat_metadata = articleData.Metadata |> JsonConvert.DeserializeObject<MicroformatMetadata> 
                content = [|articleData.Content|]
            })
    return articles        
}



/// Load list of articles from the database
let getArticlesFromDB connectionString userName = task {
    //let! articles = loadArticlesFromFile connectionString
    printfn "Trying to load articles from database..."
    let! articles = loadArticlesFromSQLDatabase connectionString 
    printfn "Finished!"

    let! annotated = checkAnnotationsExist connectionString userName articles

    return
        { UserName = userName
          Articles =
            annotated
            |> Array.map (fun (article, ann) ->
                { Title = getTitle article
                  ID = article.article_url
                  Text = None}, ann)
            |> List.ofArray } 
}



let loadArticleFromDB connectionString link = task {
    let! articles = loadArticlesFromFile connectionString 

    let selectedArticle = 
        articles
        |> Array.find (fun a -> a.article_url = link)
    
    // strip content off html tags
    let text = 
        selectedArticle.content
        |> Array.collect (fun textBlock ->
            textBlock.Split("<br> <br>"))
        |> Array.map (fun paragraph ->
            Regex.Replace(paragraph, "<.*?>", ""))
        |> Array.filter (fun paragraph ->
            paragraph.Trim() <> "")

   return
        { Title = getTitle selectedArticle; 
          ID = selectedArticle.article_url
          Text = Some text }
}


let saveAnnotationsToDB connectionString (annotations: ArticleAnnotations) = task {
    let! annotationBlob = getAnnotationsBlob connectionString annotations.User.UserName annotations.ArticleID
    do! annotationBlob.UploadTextAsync(FableJson.toJson annotations)
    return ()
}

let deleteAnnotationsFromDB connectionString (annotations: ArticleAnnotations) = task {
    let! annotationBlob = getAnnotationsBlob connectionString annotations.User.UserName annotations.ArticleID
    return! annotationBlob.DeleteIfExistsAsync()
}

let loadArticleAnnotationsFromDB connectionString articleId userName  = task {
    let! annotationBlob = getExistingAnnotationBlob connectionString userName articleId
    match annotationBlob with
    | Some blob ->
        let! text = blob.DownloadTextAsync()
        let ann = 
            text
            |> FableJson.ofJson<ArticleAnnotations>
        return Some ann
    | None -> 
        return None
}


module private StateManagement =
    let getStateBlob ( connectionString: AzureConnection) name = task {
        let client = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
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


let IsValidUser ( connectionString : AzureConnection) userName password = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("sample-crawl")

    let! text = task {
        let blob = container.GetBlockBlobReference ("users.csv")
        return! blob.DownloadTextAsync()
    }

    let data = 
        text.Split '\n'
        |> fun t -> t.[1..] // split header
        |> Array.filter (fun line -> line <> "")
        |> Array.map (fun line -> line.Split ',' |> Array.map (fun s -> s.Trim()))
        |> Array.filter (fun line ->
            line.[1] = userName && line.[2] = password)
    if data.Length <> 1 then 
        return None
    else    
        let userData = data |> Array.exactlyOne
        let user = {
            UserName = userData.[1]
            Proficiency = 
                match userData.[3] with
                | "Training" -> Training
                | "User" -> User
                | "Expert" -> Expert
                | _ -> Training
            Token = ServerCode.JsonWebToken.encode (
                     { UserName = userData.[1] } : ServerTypes.UserRights
                    )            
        }    
        return Some user
}