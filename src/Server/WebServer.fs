/// Functions for managing the Suave web server.
module ServerCode.WebServer

open ServerCode
open ServerCode.ServerUrls
open Giraffe
open Giraffe.TokenRouter
open RequestErrors
open Microsoft.AspNetCore.Http

/// Start the web server and connect to database
let webApp databaseType root =
    let startupTime = System.DateTime.UtcNow

    let db = Database.getDatabase databaseType startupTime
    let apiPathPrefix = PathString("/api")
    let notfound: HttpHandler =
        fun next ctx ->
            if ctx.Request.Path.StartsWithSegments(apiPathPrefix) then
                NOT_FOUND "Page not found" next ctx
            else
                Pages.notfound next ctx

    router notfound [
        GET [
            route PageUrls.Home Pages.home

            route APIUrls.Annotations (Auth.requiresJwtTokenForAPI (Annotations.getAnnotations db.LoadAnnotations))
            route APIUrls.ResetTime (Annotations.getResetTime db.GetLastResetTime)

            //route APIUrls.Article (Article.getArticle db.LoadArticle)
        ]

        POST [
            route APIUrls.Login Auth.login
            //route APIUrls.Annotations (Auth.requiresJwtTokenForAPI (Annotations.postAnnotations db.SaveAnnotations))
            route APIUrls.Article (Auth.requiresJwtTokenForAPI (Article.getArticle db.LoadArticle db.LoadArticleAnnotations))
            route APIUrls.Answers (Article.postAnswers db.SaveAnnotations)
        ]
    ]
