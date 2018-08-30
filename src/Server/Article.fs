/// Wish list API web parts and data access functions.
module ServerCode.Article

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Giraffe
open ServerCode.Domain
open ServerTypes

/// Handle the POST on /api/article
let getArticle (loadArticleFromDB : string -> Task<Domain.Article>) (loadArticleAnnotationsFromDB : string -> string -> Task<Domain.ArticleAnnotations option>) (token : UserRights) : HttpHandler =
     fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! article = ctx.BindModelAsync<Domain.Article>()
            let! fullArticle = loadArticleFromDB article.ID
            // TODO: Load existing annotations for the user
            let! articleAnnotation = loadArticleAnnotationsFromDB article.ID token.UserName
            
            match articleAnnotation with
            | None -> return! ctx.WriteJsonAsync (fullArticle, None)
            | Some a -> return! ctx.WriteJsonAsync (fullArticle, Some a)
            
            //let! article = loadArticleFromDB article.Link
            //return! ctx.WriteJsonAsync fullArticle
        }

//let private invalidArticle =
//    RequestErrors.BAD_REQUEST "Article is not valid"

let inline private forbiddenArticle username =
    sprintf "Article is not matching user %s" username
    |> RequestErrors.FORBIDDEN

/// Handle the POST on /api/article
(*
let postArticle (saveAnnotationsToDB: Annotations -> Task<unit>) (token : UserRights) : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! annotations = ctx.BindJsonAsync<Domain.Annotations>()

            match token.UserName.Equals annotations.UserName with
            | true ->  return! invalidAnnotations next ctx
            | false -> return! invalidAnnotations next ctx
        }

/// Retrieve the last time the wish list was reset.
let getResetTime (getLastResetTime: unit -> Task<System.DateTime>) : HttpHandler =
    fun next ctx ->
        task {
            let! lastResetTime = getLastResetTime()
            return! ctx.WriteJsonAsync({ Time = lastResetTime })
        }
*)        

//TODO

let postAnswers (saveToDB : ArticleAnnotations -> Task<unit>) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! answers = ctx.BindJsonAsync<Domain.ArticleAnnotations>()
            let! result = saveToDB answers
            return! ctx.WriteJsonAsync({ Success = true })
        }