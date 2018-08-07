/// Wish list API web parts and data access functions.
module ServerCode.Article

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Giraffe
open ServerCode.Domain
open ServerTypes

/// Handle the GET on /api/article
let getArticle (loadArticleFromDB : Article -> Task<Article>) (article : Article) : HttpHandler =
     fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! article = loadArticleFromDB article
            return! ctx.WriteJsonAsync article
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