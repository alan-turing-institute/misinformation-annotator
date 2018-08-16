module ServerCode.Storage.FileSystem

open System.IO
open ServerCode
open ServerCode.Domain

/// Get the file name used to store the data for a specific user
let getJSONFileName userName = sprintf "./temp/db/%s.json" userName

/// Get a list of articles
let getArticles userName = 
    // TODO: determine which articles are assigned to the user
    Directory.GetFiles("./temp/articles/", "*.html")

let getAnnotationsFromDB userName =
    let fi = FileInfo(getJSONFileName userName)
    if not fi.Exists then Defaults.defaultAnnotations userName
    else
        File.ReadAllText(fi.FullName)
        |> FableJson.ofJson<ArticleList>

let saveAnnotationsToDB annotations =
    let fi = FileInfo(getJSONFileName annotations.UserName)
    if not fi.Directory.Exists then
        fi.Directory.Create()
    File.WriteAllText(fi.FullName, FableJson.toJson annotations)

let getArticlesFromDB userName =
    let articles = getArticles userName
    { UserName = userName
      Articles = 
        articles
        |> Array.map (fun filename -> 
            { Title = File.ReadAllLines(filename).[0].Replace("<h1>", "").Replace("</h1>","")
              ID = filename 
              Text = None })
        |> List.ofArray
    }

let loadArticleFromDB link =
    let contents = File.ReadAllLines(link)
    let heading = contents.[0].Replace("<h1>", "").Replace("</h1>","")
    let text = 
        contents.[1..]
        |> Array.map (fun line -> line.Replace("<p>", "").Replace("</p>", ""))
    { Title = heading; Text = Some text; ID = link }