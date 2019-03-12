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
open System.Text.RegularExpressions
open System.Data
open Fable.Helpers.React
open Fable.Helpers.React.Props
open FSharp.Data


type AzureConnection = {
    BlobConnection : string
    SqlConnection : string 
}

[<Literal>]
let AnnotatedProportion = 0.5

[<Literal>]
let ConflictCountThreshold = 3

// ==============================================================================================
// Html parsing code to format articles

let getElementId attributes =
    attributes 
    |> List.choose (fun (HtmlAttribute(name, value)) -> 
        if name = "data-node-index" then Some value else None)
    |> List.exactlyOne

let rec transformHtmlFormat (contents: HtmlNode list) =
    [ for el in contents do
        match el with 
        | HtmlElement(name, attributes, elements) ->
            yield SimpleHtmlElement(name, (getElementId attributes), transformHtmlFormat elements, false) 
        | HtmlCData _ -> ()
        | HtmlComment _ -> ()
        | HtmlText x -> yield SimpleHtmlText(x)
    ]

let rec markLeafNodes (contents : SimpleHtmlNode list) =
    [ for el in contents do
        match el with 
        | SimpleHtmlText(x) -> yield (el, true)
        | SimpleHtmlElement(name, id, elements, _) ->
            let elements', areLeafs = markLeafNodes elements |> List.unzip
            yield SimpleHtmlElement(name, id, elements', (areLeafs |> List.fold (||) false )), false
    ]

let parseArticleData (rdr: SqlDataReader) (assignmentType: ArticleAssignment) includeContent = 
    [| while rdr.Read() do 
        
        let contents = 
            if includeContent then
                let articleContents = rdr.GetString(3)

                let (HtmlDocument(_, elements)) = HtmlDocument.Parse(articleContents)
                let parsedContents : ArticleText = transformHtmlFormat elements
                markLeafNodes parsedContents 
                |> List.unzip 
                |> fst
                |> Some
            else None

        yield { 
          ID = rdr.GetString(0)
          Title = rdr.GetString(1)
          SourceWebsite = rdr.GetString(2)
          AssignmentType = assignmentType
          Text = contents
                 } |]    

// ==============================================================================================

let selectArticle articleId sqlConn assignmentType includeText =
  use conn = new System.Data.SqlClient.SqlConnection(sqlConn)
  conn.Open()

  let command = "SELECT article_url, title, site_name, plain_content FROM [articles_v5] WHERE [article_url] = @ArticleId;" 
  use cmd = new SqlCommand(command, conn)
  cmd.Parameters.AddWithValue("@ArticleId", articleId) |> ignore
  
  let rdr = cmd.ExecuteReader()
  let result = 
    let parsed = 
          parseArticleData rdr assignmentType includeText
    parsed |> Array.exactlyOne
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
SELECT articles_v5.article_url, title, site_name, plain_content FROM [articles_v5] 
INNER JOIN unfinished_articles ON articles_v5.article_url = unfinished_articles.article_url"
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr Unfinished false
    conn.Close()
    result


let selectConflictingArticle connectionString userName =
    // Select an article that has two annotations with conflicting number of sources identified
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH annotated_by_user AS (
    SELECT article_url FROM [annotations]
    WHERE user_id = @UserId
),
conflicts AS (
    SELECT DISTINCT article_url
    FROM [annotations]
    WHERE article_url IN (
        SELECT article_url FROM [annotations]
        WHERE article_url NOT IN (SELECT * FROM annotated_by_user) AND num_sources IS NOT NULL
        GROUP BY article_url
        HAVING 
            COUNT(*) = 2 AND -- there are two annotations
            MAX(num_sources) - MIN(num_sources) > @Threshold -- difference between them is larger than threshold
    ) 
)
SELECT TOP(1) articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] INNER JOIN conflicts 
ON articles_v5.article_url = conflicts.article_url"  

    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore
    cmd.Parameters.AddWithValue("@Threshold", ConflictCountThreshold) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr NextArticle false
    conn.Close()
    result

let selectNextTrainingArticle connectionString userName =
    // Select article in the training batch that was not annotated by the current user
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH user_annotations AS (
    SELECT article_url, annotation FROM [annotations]
    WHERE user_id = @UserId
), 
training_articles AS (
    SELECT batch_article_test.article_url 
    FROM [batch_article_test] 
        LEFT JOIN user_annotations
        ON batch_article_test.article_url = user_annotations.article_url
    WHERE minibatch_id = 0 AND annotation IS NULL
)
SELECT TOP(1) articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] 
WHERE articles_v5.article_url IN (SELECT article_url FROM training_articles)"  

    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr NextArticle false
    conn.Close()
    result

let selectFinishedArticles userName connectionString maxCount =
    // Select all articles annotated by the current user previously
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
WITH annotated AS (
    SELECT TOP(@Count) article_url FROM [annotations]
    WHERE user_id = @UserId AND num_sources IS NOT NULL
    ORDER BY updated_date DESC
)
SELECT articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] INNER JOIN annotated 
ON articles_v5.article_url = annotated.article_url"  

    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore
    cmd.Parameters.AddWithValue("@Count", maxCount) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr PreviouslyAnnotated false
    conn.Close()
    result

/// Select next article for standard annotation
let selectNextStandardArticle connectionString userName =
    printfn "Choosing new article to annotate"
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()
    let command = "
DECLARE @selected_url NVARCHAR(800);

-- all articles annotated by user
WITH user_annotations AS ( 
    SELECT article_url, created_date
    FROM [annotations]
    WHERE user_id = @UserId   
),
-- all articles that need annotations, regardless of user
all_articles_that_need_annotation AS (   
    SELECT minibatch_id, batch_article_test.article_url
    FROM [batch_article_test] 
        LEFT JOIN annotations ON batch_article_test.article_url = annotations.article_url
    GROUP BY batch_article_test.article_url, batch_article_test.minibatch_id
    HAVING COUNT(*) < 2 AND minibatch_id > 0
),
-- all articles that need annotation, excluding articles already annotated by the current user
batch_article_to_annotate AS (
    SELECT * 
    FROM all_articles_that_need_annotation 
    WHERE article_url NOT IN (SELECT article_url FROM user_annotations)
),
-- count proportion of user-annotated articles in the batches that require adding an annotation
annotated_in_minibatch AS ( 
    SELECT minibatch_id, CAST(COUNT(batch_article_to_annotate.article_url) AS FLOAT) AS n_total, CAST(COUNT(user_annotations.created_date) AS FLOAT) AS n_annotated
    FROM batch_article_to_annotate 
        LEFT JOIN user_annotations
        ON user_annotations.article_url = batch_article_to_annotate.article_url
    GROUP BY minibatch_id
),
-- take the first minibatch that needs annotations added from the current user
selected_minibatch AS ( 
    SELECT TOP(1) annotated_in_minibatch.minibatch_id, n_annotated/n_total AS proportion
    FROM annotated_in_minibatch 
        INNER JOIN batch_info_test ON annotated_in_minibatch.minibatch_id = batch_info_test.minibatch_id
    WHERE n_annotated/n_total < @AnnotatedProportion AND priority > 0
    ORDER BY priority
),
-- articles in the selected minibatch
potential_articles AS (
    SELECT article_url 
    FROM [batch_article_test]
    WHERE minibatch_id IN (SELECT minibatch_id FROM selected_minibatch)
),
annotated_counts AS (  -- Count how many annotations are there for each article, ignoring articles annotated by the user
    SELECT potential_articles.article_url, COUNT(created_date) AS n 
    FROM potential_articles LEFT JOIN [annotations] ON annotations.article_url = potential_articles.article_url
    WHERE potential_articles.article_url NOT IN (SELECT article_url FROM [annotations] WHERE user_id = @UserId)
    GROUP BY potential_articles.article_url
    HAVING COUNT(*) < 2 
)
-- sort articles randomly, starting with articles with no annotations
SELECT @selected_url = (
    SELECT TOP(1) article_url
    FROM annotated_counts
    GROUP BY article_url, n
    ORDER BY n, NEWID()  
);
SELECT articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] 
WHERE articles_v5.article_url = @selected_url
"  
    // TODO: test properly
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore
    cmd.Parameters.AddWithValue("@AnnotatedProportion", AnnotatedProportion) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr NextArticle false
    conn.Close()
    result


// Randomize the order of articles as they are shown to the user
let shuffle articles =
    let rnd = new System.Random()
    articles
    |> Array.sortBy (fun _ -> rnd.Next())

let loadArticlesFromSQLDatabase connectionString (userData: UserData) articleType = task {
    // Find articles that should be displayed to the specific user
    let maxArticlesToShow = 100

    match articleType with
    | PreviouslyAnnotated ->
        // load all previously annotated articles
        let articles = 
            selectFinishedArticles userData.UserName connectionString maxArticlesToShow
        return articles

    | Unfinished ->
        // check if there is a previously assigned article without annotation
        // based on the logic of the algorithm, there should be only one such article
        let articles = 
            selectUnfinishedArticles connectionString userData.UserName
        if articles.Length > 1 then 
            printfn "Warning: user %s has more than 1 unfinished annotation in the database." userData.UserName
        return articles

    | NextArticle ->
        // Main user assignment algorithm

        match userData.Proficiency with
        | Training _ -> 
            printfn "Next training article"
            // load article from the training batch
            return 
                selectNextTrainingArticle connectionString userData.UserName

        | Validation _ ->
            printfn "Next validation article"
            // TODO
            return [||]       
        
        | Standard User ->
            // standard user
            return
                selectNextStandardArticle connectionString userData.UserName

        | Standard Expert ->
            // expert user
            let articles = 
                let conflicts = 
                    selectConflictingArticle connectionString userData.UserName
                if conflicts.Length = 0 then
                    printfn "No conflicting articles found - selecting next standard article"
                    selectNextStandardArticle connectionString userData.UserName
                else
                    conflicts
            return articles


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
let getArticlesFromDB connectionString (userData : Domain.UserData) (articleType: Domain.ArticleAssignment) = task {

    let! articles = loadArticlesFromSQLDatabase connectionString userData articleType
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
   let article = selectArticle link connectionString.SqlConnection NextArticle true
   return
        article
}


let saveAnnotationsToDB connectionString (annotations: ArticleAnnotations) = task {
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let annotationText = FableJson.toJson annotations

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
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let command = "SELECT * FROM [users] WHERE username = @UserId AND password = @Password"
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", userName) |> ignore
    cmd.Parameters.AddWithValue("@Password", password) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = 
        [| while rdr.Read() do 
            let proficiency = 
                match rdr.GetString(3) with
                | "User" -> User
                | "Expert" -> Expert
                | _ -> User 
            let user = {
                UserName = rdr.GetString(0)
                Proficiency = 
                    match rdr.GetInt32(4) with // Passed training, i.e. went through the training batch
                    | 0 -> Training proficiency
                    | 1 -> Validation proficiency
                    | 2 -> Standard proficiency
                    | _ -> Training proficiency
                Token = ServerCode.JsonWebToken.encode (
                         { UserName = rdr.GetString(0) } : ServerTypes.UserRights
                        )            
                }    
            yield user |]  

    conn.Close()
    if result.Length = 0 then 
        return None
    else
        return (Some result.[0])

    }

let FlagArticle ( connectionString : AzureConnection) (flaggedArticle: Domain.FlaggedArticle) = task {
    let blobClient = (CloudStorageAccount.Parse connectionString.BlobConnection).CreateCloudBlobClient()
    let container = blobClient.GetContainerReference("log")  

    let blob = container.GetBlockBlobReference ("flagged_articles.csv")
    let! text = task {    
        return! blob.DownloadTextAsync()
    }
    let text' = text + sprintf "%s,%s,%s\n" flaggedArticle.User.UserName flaggedArticle.ArticleID (System.DateTime.Now.ToString())
    let! result = blob.UploadTextAsync(text')
    let! exists = blob.ExistsAsync()
    return exists
}

// TODO
let UpdateUserStatus ( connectionString : AzureConnection)  (user: Domain.UserData) = task {
    use conn = new System.Data.SqlClient.SqlConnection(connectionString.SqlConnection)
    conn.Open()

    let command = "UPDATE [users]
    SET passed_training = 1 
    WHERE username = @UserId"  

    let cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@UserId", user.UserName) |> ignore

    let result = cmd.ExecuteNonQuery() 
    conn.Close()
    if result = 1 then return true else return false
}