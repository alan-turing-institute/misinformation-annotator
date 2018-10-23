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

//type Metadata = JsonProvider<"""{"microdata": [], "json-ld": [], "opengraph": [{"namespace": {"og": "http://ogp.me/ns#", "fb": "http://ogp.me/ns/fb#", "article": "http://ogp.me/ns/article#"}, "properties": [["fb:app_id", "529217710562597"], ["fb:pages", "146422995398181"], ["og:type", "article"], ["og:title", "Yale Alumnae Stand Up To Kavanaugh, Support His Latest Accuser By The Hundreds"], ["og:url", "http://addictinginfo.com/2018/09/24/yale-alumnae-stand-up-to-kavanaugh-support-his-latest-accuser-by-the-hundreds/"], ["og:description", "But of course, the GOP-led Senate seems to be unmoved by any of this. "], ["og:site_name", "AddictingInfo"], ["og:image", "http://addictinginfo.com/wp-content/uploads/2018/09/Screen-Shot-2018-09-23-at-7.52.05-PM.jpg"], ["og:locale", "en_US"], ["article:author", "http://facebook.com/shannonforequality"], ["article:publisher", "https://www.facebook.com/politicsbyjustin/"], ["fb:op-recirculation-ads", "placement_id=470010220032878_503313496702550"]]}], "microformat": [{"type": ["h-entry"], "properties": {"category": ["news"], "name": ["Yale Alumnae Stand Up To Kavanaugh, Support His Latest Accuser By The Hundreds"], "url": ["http://addictinginfo.com/2018/09/24/yale-alumnae-stand-up-to-kavanaugh-support-his-latest-accuser-by-the-hundreds/"], "updated": ["2018-09-24T14:42:34+00:00"], "published": ["2018-09-24T14:42:34+00:00"], "content": [{"html": "<p>Supreme Court nominee Brett Kavanaugh is embroiled in scandals surrounding his alleged past as a sexual assaulter. Aside from Dr. Christine Blasey Ford\u2019s accusation that Kavanaugh violently </div>", "value": "Supreme Court nominee Brett Kavanaugh is embroiled in scandals surrounding his alleged past as a sexual assaulter. Aside from Dr. Christine Blasey Ford\u2019s accusation that Kavanaugh violently sexually assaulted her when they were both in high sTwitter"}]}, "children": [{"type": ["h-card"], "properties": {"photo": ["http://0.gravatar.com/avatar/33eb8e63d6f325bdcf229ce66edc227a?s=70&d=identicon&r=g"], "name": ["Shannon Barber"], "url": ["http://addictinginfo.com/author/shannon-barber/"]}}]}], "rdfa": [{"@id": "http://addictinginfo.com/2018/09/24/yale-alumnae-stand-up-to-kavanaugh-support-his-latest-accuser-by-the-hundreds/", "article:author": [{"@value": "http://facebook.com/shannonforequality"}], "article:publisher": [{"@value": "https://www.facebook.com/politicsbyjustin/"}], "http://ogp.me/ns#description": [{"@value": "But of course, the GOP-led Senate seems to be unmoved by any of this. "}], "http://ogp.me/ns#image": [{"@value": "http://addictinginfo.com/wp-content/uploads/2018/09/Screen-Shot-2018-09-23-at-7.52.05-PM.jpg"}], "http://ogp.me/ns#locale": [{"@value": "en_US"}], "http://ogp.me/ns#site_name": [{"@value": "AddictingInfo"}], "http://ogp.me/ns#title": [{"@value": "Yale Alumnae Stand Up To Kavanaugh, Support His Latest Accuser By The Hundreds"}], "http://ogp.me/ns#type": [{"@value": "article"}], "http://ogp.me/ns#url": [{"@value": "http://addictinginfo.com/2018/09/24/yale-alumnae-stand-up-to-kavanaugh-support-his-latest-accuser-by-the-hundreds/"}], "http://ogp.me/ns/fb#app_id": [{"@value": "529217710562597"}], "http://ogp.me/ns/fb#op-recirculation-ads": [{"@value": "placement_id=470010220032878_503313496702550"}], "http://ogp.me/ns/fb#pages": [{"@value": "146422995398181"}], "https://api.w.org/": [{"@id": "http://addictinginfo.com/wp-json/"}]}]}""">

type AzureConnection = {
    BlobConnection : string
    SqlConnection : string 
}

let parseArticleData (rdr: SqlDataReader) includeContent = 
    [| while rdr.Read() do 
        yield { ID = rdr.GetString(0)
                Title = rdr.GetString(1)
                SourceWebsite = rdr.GetString(2)
                Text = (if includeContent then Some [| rdr.GetString(3) |] else None)
                 } |]    


let selectArticle articleId sqlConn includeText =
  use conn = new System.Data.SqlClient.SqlConnection(sqlConn)
  conn.Open()

  let command = "SELECT article_url, title, site_name, plain_content FROM [articles_v2] WHERE [article_url] = @ArticleId;" 
  use cmd = new SqlCommand(command, conn)
  cmd.Parameters.AddWithValue("@ArticleId", articleId) |> ignore

  let rdr = cmd.ExecuteReader()
  let result =
      parseArticleData rdr includeText
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
    let result = parseArticleData rdr false
    conn.Close()
    result

let selectAddAnnotationArticles connectionString userName =
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
    let result = parseArticleData rdr false
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
    ORDER BY batch_id DESC
)
SELECT TOP (@ArticleCount) articles_v2.article_url, title, site_name, plain_content 
FROM [articles_v2] RIGHT JOIN selected_articles 
ON articles_v2.article_url = selected_articles.article_url
"    
    use cmd = new SqlCommand(command, conn)
    cmd.Parameters.AddWithValue("@ArticleCount", articlesToShow) |> ignore

    let rdr = cmd.ExecuteReader()
    let result = parseArticleData rdr false
    conn.Close()
    result

let loadArticlesFromSQLDatabase connectionString userData = task {
    // TODO: Find articles that should be displayed to the specific user
    let articlesToShow = 20

    match userData.Proficiency with
    | Training //->
        // let! results = task {
        //     let articles = selectNumArticlesPerSite 20 connectionString.SqlConnection
        //     return articles
        // }
        // return results
        
    | User | Expert ->
        // 1. Select articles assigned to user with unfinished annotations
        let articlesUnfinished =
            selectUnfinishedArticles connectionString userData.UserName

        // 2. Select articles that have annotation by only one user
        let articlesUncomplete =
            selectAddAnnotationArticles connectionString userData.UserName

        // 3. Select the remaining articles from the current batch in the articles database
        let articlesNew =
            let n = articlesToShow - articlesUnfinished.Length - articlesUncomplete.Length
            if n > 0 then 
                selectNewArticles n connectionString
            else    
                [||]

        // -- randomize the order of articles as they are shown to the user
        let allArticles =
            let rnd = new System.Random()
            [| articlesUnfinished; 
               articlesUncomplete; 
               articlesNew 
               |]
            |> Array.concat
            |> Array.sortBy (fun _ -> rnd.Next())

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
    let article = selectArticle link connectionString.SqlConnection true

    // // strip content off html tags
    // let text = 
    //     article.Content.Value.Replace("[\"","").Replace("\"]","")
    //     |> fun s -> s.Split("</p>\", \"<p>")
    //     |> Array.collect (fun tx -> tx.Split('\n'))
    //     |> Array.map (fun paragraph ->
    //         Regex.Replace(paragraph, "<.*?>", ""))
    //     |> Array.filter (fun paragraph ->
    //         paragraph.Trim() <> "")
    //     |> Array.map toUnicode

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