module ServerCode.Storage.FileSystem

open System.IO
open ServerCode
open ServerCode.Domain
open Fable.AST.Fable

/// Get the file name used to store the data for a specific user
//let getJSONFileName userName = sprintf "./temp/db/%s.json" userName
let getJSONFileName userName (articleId: string) = 
    let id = articleId.Replace("/", "-")
    sprintf "./temp/db/%s-%s.json" userName id

/// Get a list of articles
let getArticles userName = 
    // TODO: determine which articles are assigned to the user
    Directory.GetFiles("./temp/articles/", "*.html")

let getAnnotations userName =
    Directory.GetFiles("./temp/db/", userName + "*.json")
    |> Array.map (fun s -> s.Replace(userName + "-", ""))

let saveAnnotationsToDB (annotations: ArticleAnnotations) =
    let fi = FileInfo(getJSONFileName annotations.User.UserName annotations.ArticleID)
    File.WriteAllText(fi.FullName, FableJson.toJson annotations)
    ()

let deleteAnnotationsFromDB (annotations: ArticleAnnotations) =
    let fi = FileInfo(getJSONFileName annotations.User.UserName annotations.ArticleID)
    File.Delete(fi.FullName)
    not (File.Exists(fi.FullName))

let getArticlesFromDB (userData : Domain.UserData) =
    let userName = userData.UserName
    let articles = getArticles userName
    let annotations = getAnnotations userName

    let annotated =
        articles 
        |> Array.map (fun filename ->
            let id = filename.Replace("/", "-") + ".json"
            let isAnnotated =
                (false, annotations)
                ||> Array.fold (fun state ann -> state || ann.EndsWith id)
            if isAnnotated then filename, true else filename, false)

    { UserName = userName
      Articles = 
        annotated
        |> Array.map (fun (filename, ann) -> 
            { Title = File.ReadAllLines(filename).[0].Replace("<h1>", "").Replace("</h1>","")
              ID = filename 
              Text = None
              SourceWebsite = "Just testing"
              AssignmentType = Standard }, ann)
        |> List.ofArray
    }



let loadArticleFromDB link =
    let contents = File.ReadAllLines(link)
    let heading = contents.[0].Replace("<h1>", "").Replace("</h1>","")
    let text = 
        contents.[1..]
        |> Array.map (fun line -> line.Replace("<p>", "").Replace("</p>", ""))
    { Title = heading; Text = Some text; ID = link; SourceWebsite = "Just testing" ; AssignmentType = Standard }


let loadArticleAnnotationsFromDB articleId userName : ArticleAnnotations option =
    let fi = FileInfo(getJSONFileName userName articleId)
    if fi.Exists then
        File.ReadAllText(fi.FullName)
        |> FableJson.ofJson<ArticleAnnotations>
        |> Some
    else 
        None

let IsValidUser userName password = 
    if ((userName = "test" && password = "test") ||
        (userName = "test2" && password = "test2")) then
        Some (
          {
            UserName = userName
            Proficiency = UserProficiency.Training
            Token =
                ServerCode.JsonWebToken.encode (
                    { UserName = userName } : ServerTypes.UserRights
                )
          } : Domain.UserData)
    else 
        None    
    