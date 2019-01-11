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
            
            let! articleAnnotation = loadArticleAnnotationsFromDB article.ID token.UserName
            
            match articleAnnotation with
            | None -> 
                return! ctx.WriteJsonAsync (fullArticle, None)
            | Some a -> 
                printfn "Managed to get annotations for article: %s" fullArticle.Title
                return! ctx.WriteJsonAsync (fullArticle, Some a)
            
            //let! article = loadArticleFromDB article.Link
            //return! ctx.WriteJsonAsync fullArticle
        }

//let private invalidArticle =
//    RequestErrors.BAD_REQUEST "Article is not valid"

let inline private forbiddenArticle username =
    sprintf "Article is not matching user %s" username
    |> RequestErrors.FORBIDDEN


let postAnswers (saveToDB : ArticleAnnotations -> Task<unit>)  (deleteFromDB : ArticleAnnotations -> Task<bool>) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            
            let! annaction = ctx.BindJsonAsync<Domain.ArticleAnnotationAction>()
            match annaction.Action with
            | Save ->
                let! result = saveToDB annaction.Annotations
                return! ctx.WriteJsonAsync({ Success = true })
            | Delete ->
                let! result = deleteFromDB annaction.Annotations
                return! ctx.WriteJsonAsync({ Success = result })
        }

let flagIncorrectlyParsed (flagInDB : FlaggedArticle -> Task<bool>) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! flagged = ctx.BindJsonAsync<Domain.FlaggedArticle>()
            let! result = flagInDB flagged
            return! ctx.WriteJsonAsync({ Success = result })
        }        