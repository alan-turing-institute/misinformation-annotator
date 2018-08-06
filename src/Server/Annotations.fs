/// Wish list API web parts and data access functions.
module ServerCode.Annotations

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Giraffe
open ServerCode.Domain
open ServerTypes

/// Handle the GET on /api/annotations
let getAnnotations (getArticlesFromDB : string -> Task<Annotations>) (token : UserRights) : HttpHandler =
     fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! annotations = getArticlesFromDB token.UserName
            return! ctx.WriteJsonAsync annotations
        }

let private invalidAnnotations =
    RequestErrors.BAD_REQUEST "Annotations is not valid"

let inline private forbiddenAnnotations username =
    sprintf "Annotations is not matching user %s" username
    |> RequestErrors.FORBIDDEN

/// Handle the POST on /api/annotations
let postAnnotations (saveAnnotationsToDB: Annotations -> Task<unit>) (token : UserRights) : HttpHandler =
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