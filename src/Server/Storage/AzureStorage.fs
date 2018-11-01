module ServerCode.Storage.AzureStorage

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open ServerCode
open ServerCode.Domain
open ServerCode.FableJson
open System
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive
open Newtonsoft.Json
open Microsoft.Extensions.Logging
open Fable.AST.Fable
open System.Text.RegularExpressions
open System.Data.SqlClient
open FSharp.Data
open System.Text.RegularExpressions
open System.Data

type AzureConnection = {
    BlobConnection : string
    SqlConnection : string 
}

let parseArticleData (rdr: SqlDataReader) (assignmentType: ArticleAssignment) includeContent = 
    [| while rdr.Read() do 
        yield { 
          ID = rdr.GetString(0)
          Title = rdr.GetString(1)
          SourceWebsite = rdr.GetString(2)
          AssignmentType = assignmentType
          Text = 
            (if includeContent then 
               Some (ofJson<string[]>(rdr.GetString(3))) 
             else None)
                 } |]    


let selectArticle articleId sqlConn assignmentType includeText =
  use conn = new System.Data.SqlClient.SqlConnection(sqlConn)
  conn.Open()

  let command = "SELECT article_url, title, site_name, plain_content FROM [articles_v2] WHERE [article_url] = @ArticleId;" 
  use cmd = new SqlCommand(command, conn)
  cmd.Parameters.AddWithValue("@ArticleId", articleId) |> ignore

  let rdr = cmd.ExecuteReader()
  let result =
      parseArticleData rdr assignmentType includeText
      |> Array.exactlyOne
  conn.Close()  
  result

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

let checkAnnotationsExist (connectionString : AzureConnection) userName articles  = task {

    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "SELECT article_url FROM [annotations] WHERE user_id = @UserId AND num_sources IS NOT NULL;"
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = 
        [| while rdr.Read() do 
            let article_url = rdr.GetString(0)
            yield article_url |]
        |> set
    let annotations =
        articles
        |> Array.map (fun article ->  
            article, 
            if result.Contains(article.ID) then true else false)
    conn.Close()
    return annotations        
}
    
let getArticlesBlob (connectionString : AzureConnection) = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("sample-crawl")
    let articleBlob = container.GetBlockBlobReference "articles/misinformation.txt"
    return articleBlob }


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

let selectUnfinishedArticles connectionString userName =
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH unfinished_articles AS (
    SELECT article_url 
    FROM [annotations] 
    WHERE user_id =@UserId AND annotation IS NULL
)
SELECT articles_v2.article_url, title, site_name, plain_content FROM [articles_v2] 
RIGHT JOIN unfinished_articles ON articles_v2.article_url = unfinished_articles.article_url"
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr Unfinished false
    conn.Close()
    result

let selectAddAnnotationArticles connectionString userName =
    // articles that have only one annotation right now
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH unfinished_articles AS (
    SELECT article_url 
    FROM [annotations] 
    GROUP BY article_url 
    HAVING (COUNT(article_url) = 1)
),
to_finish AS (
SELECT article_url
FROM [annotations]
WHERE user_id <> @UserId 
    AND EXISTS 
     (SELECT * FROM unfinished_articles 
      WHERE unfinished_articles.article_url = annotations.article_url)
)
SELECT articles_v2.article_url, title, site_name, plain_content 
FROM [articles_v2] RIGHT JOIN to_finish 
ON to_finish.article_url = articles_v2.article_url"

    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore    

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr Standard false
    conn.Close()
    result

let selectNewArticles articlesToShow connectionString =
    // Select all articles in active batches that are not currently being annotated already
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH selected_articles AS (
    SELECT TOP 100 article_url FROM [batch_article]
    WHERE
    article_url NOT IN (
            SELECT article_url FROM [annotations]
    ) 
    AND
    batch_id IN (
            SELECT batch_id FROM [batch] WHERE active = 1
    )
    ORDER BY batch_id DESC, newid() 
)
SELECT TOP (@ArticleCount) articles_v2.article_url, title, site_name, plain_content 
FROM [articles_v2] RIGHT JOIN selected_articles 
ON articles_v2.article_url = selected_articles.article_url
"    
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@ArticleCount", articlesToShow) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr Standard false
    conn.Close()
    result

let selectConflictingArticles userName connectionString =
    // Select all articles that have two annotations with conflicting number of sources identified
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH conflicts AS (
    SELECT DISTINCT article_url
    FROM [annotations]
    WHERE article_url IN (
        SELECT article_url FROM [annotations]
        WHERE user_id <> @UserId
        GROUP BY article_url
        HAVING COUNT(distinct num_sources) = 2
    ) 
)
SELECT articles_v2.article_url, title, site_name, plain_content 
FROM [articles_v2] RIGHT JOIN conflicts 
ON articles_v2.article_url = conflicts.article_url"  

    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr ConflictingAnnotation false
    conn.Close()
    result

let selectFinishedArticles userName connectionString =
    // Select all articles that have two annotations by normal users
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH conflicts AS (
    SELECT DISTINCT article_url
    FROM [annotations]
    WHERE article_url IN (
        SELECT article_url FROM [annotations]
        WHERE user_id <> @UserId
        GROUP BY article_url
        HAVING COUNT(*) = 2
    ) 
)
SELECT articles_v2.article_url, title, site_name, plain_content 
FROM [articles_v2] RIGHT JOIN conflicts 
ON articles_v2.article_url = conflicts.article_url"  

    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr ThirdExpertAnnotation false
    conn.Close()
    result

let loadNextBatchOfArticles connectionString userName articlesToShow =
    // 1. Select articles that have annotation by only one user
    let articlesUncomplete =
        selectAddAnnotationArticles connectionString userName

    // 3. Select the remaining articles from the current batch in the articles database
    let articlesNew =
        let n = articlesToShow - articlesUncomplete.Length
        if n > 0 then 
            selectNewArticles n connectionString
        else    
            [||]

    Array.append articlesUncomplete articlesNew

let loadArticlesFromSQLDatabase connectionString userData = task {
    // TODO: Find articles that should be displayed to the specific user
    let articlesToShow = 30

    match userData.Proficiency with
    | Training //->
        // let! results = task {
        //     let articles = selectNumArticlesPerSite 20 connectionString.SqlConnection
        //     return articles
        // }
        // return results
        
    | User ->
        // 1. Select articles assigned to user with unfinished annotations
        let articlesUnfinished =
            selectUnfinishedArticles connectionString userData.UserName

        // 2. Load articles that need to be completed and new articles
        let articlesOther = loadNextBatchOfArticles connectionString userData.UserName (articlesToShow - articlesUnfinished.Length)

        // -- randomize the order of articles as they are shown to the user
        let allArticles =
            let rnd = new System.Random()
            [| articlesUnfinished; 
               articlesOther 
               |]
            |> Array.concat
            //|> Array.sortBy (fun _ -> rnd.Next())

        return allArticles
    
    | Expert ->
        // 1. Select articles previously assigned to user with unfinished annotations
        let articlesUnfinished =
            selectUnfinishedArticles connectionString userData.UserName
        printfn "%d" articlesUnfinished.Length

        // 2. Select articles with conflicting annotations
        let articlesConflicts = selectConflictingArticles userData.UserName connectionString
        printfn "%d" articlesConflicts.Length

        // 3. Select articles to add third annotation
        let articlesThirdAnnotation = selectFinishedArticles userData.UserName connectionString
        printfn "%d" articlesThirdAnnotation.Length

        // 4. Select articles like for normal users
        let articlesOther = 
            loadNextBatchOfArticles connectionString userData.UserName 
                (articlesToShow - articlesUnfinished.Length - articlesConflicts.Length)// - articlesThirdAnnotation.Length)
        printfn "%d" articlesOther.Length

        let allArticles =
            let rnd = new System.Random()
            [| articlesUnfinished; 
               articlesConflicts;
               //articlesThirdAnnotation;
               articlesOther 
               |]
            |> Array.concat
            |> Array.take 30
            //|> Array.sortBy (fun _ -> rnd.Next())

        return allArticles

}

let markAssignedArticles connectionString (articles: Article []) (userData : Domain.UserData) =
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let commandText = 
       "IF (NOT EXISTS (SELECT * FROM [annotations] WHERE article_url = @Url AND user_id = @UserId))
       BEGIN
         INSERT INTO [annotations](article_url, user_id, user_proficiency,created_date) 
           VALUES (@Url, @UserId, @Proficiency, @CreatedDate)
       END"
    let cmd = new SqlCommand(commandText, conn)
    
    cmd.Parameters.Add("@Url", SqlDbType.NVarChar) |> ignore
    cmd.Parameters.Add("@UserId", SqlDbType.NVarChar) |> ignore
    cmd.Parameters.Add("@Proficiency", SqlDbType.NVarChar) |> ignore
    cmd.Parameters.Add("@CreatedDate", SqlDbType.DateTime) |> ignore

    articles 
    |> Array.iter (fun article ->
        cmd.Parameters.["@Url"].Value <- article.ID
        cmd.Parameters.["@UserId"].Value <- userData.UserName
        cmd.Parameters.["@Proficiency"].Value <- string userData.Proficiency
        cmd.Parameters.["@CreatedDate"].Value <- System.DateTime.UtcNow

        cmd.ExecuteNonQuery() |> ignore
    )


/// Load list of articles from the database
let getArticlesFromDB connectionString (userData : Domain.UserData) = task {

    let! articles = loadArticlesFromSQLDatabase connectionString userData
    let! annotated = checkAnnotationsExist connectionString userData.UserName articles
    // Mark articles as assigned to the current user
    markAssignedArticles connectionString articles userData

    let result =         
        { UserName = userData.UserName
          Articles =
            annotated
            |> List.ofArray } 

    return result
}

let loadArticleFromDB connectionString link = task {
   let article = selectArticle link connectionString.SqlConnection Standard true
   return
        article
}


let saveAnnotationsToDB connectionString (annotations: ArticleAnnotations) = task {
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let annotationText = FableJson.toJson annotations

    // TODO: Check if there already is an entry with the same user-article pair
    // and if so, then overwrite that
    let commandText = 
       "UPDATE [annotations]
       SET annotation = @Annotation, updated_date = @UpdatedDate, num_sources = @NumSources
       WHERE article_url = @Url AND user_id = @UserId; "
    let cmd = new SqlCommand(commandText, conn)
    
    cmd.Parameters.AddWithValue("@Url", annotations.ArticleID) |> ignore
    cmd.Parameters.AddWithValue("@Annotation", toJson annotations)  |> ignore
    cmd.Parameters.AddWithValue("@UserId", annotations.User.UserName)  |> ignore
    cmd.Parameters.AddWithValue("@UpdatedDate", System.DateTime.UtcNow)  |> ignore
    cmd.Parameters.AddWithValue("@NumSources", annotations.Annotations.Length)  |> ignore

    let result = cmd.ExecuteNonQuery()
    conn.Close()
    return ()
}

let deleteAnnotationsFromDB connectionString (annotations: ArticleAnnotations) = task {
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let user = annotations.User.UserName
    let id = annotations.ArticleID
    let command = "DELETE FROM [annotations] WHERE article_url = @ArticleUrl AND user_id = @UserId;"  
    let cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@ArticleUrl", annotations.ArticleID) |> ignore
    cmd.Parameters.AddWithValue("@UserId", annotations.User.UserName) |> ignore

    let result = cmd.ExecuteNonQuery()
    conn.Close()
    if result = 1 then return true else return false
}

let loadArticleAnnotationsFromDB connectionString articleId userName  = task {
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let command = "SELECT annotation FROM [annotations] WHERE article_url = @ArticleUrl AND user_id = @UserId AND annotation IS NOT NULL;" 
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@ArticleUrl", articleId) |> ignore
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = 
        [| while rdr.Read() do 
            let text = rdr.GetString(0)
            let ann = text |> FableJson.ofJson<ArticleAnnotations>
            yield Some ann |]
    conn.Close()

    return (
        match result.Length with
        | 0 -> None
        | 1 -> Array.exactlyOne result 
        | _ -> result.[0])
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

let FlagArticle ( connectionString : AzureConnection) (flaggedArticle: Domain.FlaggedArticle) = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("log")  

    let blob = container.GetBlockBlobReference ("flagged_articles.csv")
    let! text = task {    
        return! blob.DownloadTextAsync()
    }
    let text' = text + sprintf "%s,%s" flaggedArticle.User.UserName flaggedArticle.ArticleID
    let! result = blob.UploadTextAsync(text')
    let! exists = blob.ExistsAsync()
    return exists
}