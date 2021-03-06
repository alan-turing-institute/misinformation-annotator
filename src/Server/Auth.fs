/// Login web part and functions for API web part request authorisation with JWT.
module ServerCode.Auth

open System
open System.Threading.Tasks
open Giraffe
open RequestErrors
open Microsoft.AspNetCore.Http
open ServerCode.Domain

let createUserData (login : Domain.Login) =
    {
        UserName = login.UserName
        Proficiency = UserProficiency.Standard User
        Token    =
            ServerCode.JsonWebToken.encode (
                { UserName = login.UserName } : ServerTypes.UserRights
            )
    } : Domain.UserData

/// Authenticates a user and returns a token in the HTTP body.
let login (validateUser : string -> string ->  Task<UserData option>) : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! login = ctx.BindJsonAsync<Domain.Login>()
            let! result = validateUser login.UserName login.Password
            return!
                match result with
                | Some(data) ->
                    ctx.WriteJsonAsync data
                | None -> UNAUTHORIZED "Bearer" "" (sprintf "User '%s' can't be logged in." login.UserName) next ctx
        }

let private missingToken = RequestErrors.BAD_REQUEST "Request doesn't contain a JSON Web Token"
let private invalidToken = RequestErrors.FORBIDDEN "Accessing this API is not allowed"

/// Checks if the HTTP request has a valid JWT token for API.
/// On success it will invoke the given `f` function by passing in the valid token.
let requiresJwtTokenForAPI f : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        (match ctx.TryGetRequestHeader "Authorization" with
        | Some authHeader ->
            let jwt = authHeader.Replace("Bearer ", "")
            match JsonWebToken.isValid jwt with
            | Some token -> f token
            | None -> invalidToken
        | None -> missingToken) next ctx

let markUserStatusChange (updateUserStatusDB : UserData -> Task<bool>) : HttpHandler  =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! user = ctx.BindJsonAsync<Domain.UserData>()
            let! result = updateUserStatusDB user
            return! ctx.WriteJsonAsync(result)
        }    
        