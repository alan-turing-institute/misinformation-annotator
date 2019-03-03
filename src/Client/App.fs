module Client.App

open Fable.Core
open Fable.Core.JsInterop

open Fable.Import
open Fable.PowerPack
open Elmish
open Elmish.React
open Elmish.Browser.Navigation
open Elmish.HMR
open Client.Shared
open Client.Pages
open ServerCode.Domain
open Fetch.Fetch_types
open ServerCode
open ServerCode.Domain

JsInterop.importSideEffects "whatwg-fetch"
JsInterop.importSideEffects "babel-polyfill"

let handleNotFound (model: Model) =
    Browser.console.error("Error parsing url: " + Browser.window.location.href)
    ( model, Navigation.modifyUrl (toPath Page.Home) )

/// The navigation logic of the application given a page identity parsed from the .../#info
/// information in the URL.
let urlUpdate (result:Page option) (model: Model) =
    match result with
    | None ->
        handleNotFound model

    | Some Page.Login ->
        let m, cmd = Login.init model.User
        { model with PageModel = LoginModel m }, Cmd.map LoginMsg cmd

    | Some Page.Annotations ->
        match model.User with
        | Some user ->
            let m, cmd = Annotations.init(user, model.AllArticles, model.ArticleToAnnotate)
            { model with PageModel = AnnotationsModel m }, Cmd.map AnnotationsMsg cmd
        | None ->
            model, Cmd.ofMsg (Logout ())

    | Some Page.Home ->
        { model with PageModel = HomePageModel }, Cmd.none

    | Some Page.Article ->
        match model.User with 
        | Some user -> 
            match model.SelectedArticle with
            | Some article ->
                Browser.console.log("Updating article?")
                let m, cmd = Article.init user article
                { model with PageModel = ArticleModel m }, Cmd.map ArticleMsg cmd
            | None ->
                Browser.console.log("Article - nothing changed")
                model, Cmd.none
        | None -> 
            model, Cmd.ofMsg (Logout())

            

let loadUser () : UserData option =
    BrowserLocalStorage.load "user"

let saveUserCmd user =
    Cmd.ofFunc (BrowserLocalStorage.save "user") user (fun _ -> LoggedIn user) StorageFailure

let deleteUserCmd =
    Cmd.ofFunc BrowserLocalStorage.delete "user" (fun _ -> LoggedOut) StorageFailure



let loadArticles (user: UserData, articleType: ArticleAssignment) =
    promise {
        let url = ServerUrls.APIUrls.Annotations
        let body = toJson (user, articleType)
        let props =
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + user.Token)
                HttpRequestHeaders.ContentType "application/json" ]
              RequestProperties.Body !^body ]

        return! Fetch.fetchAs<ArticleList> url props
    }

let loadArticlesCmd user articleType =
    Browser.console.log("Requesting articles")
    Cmd.ofPromise loadArticles (user, articleType) FetchedArticles FetchError

let loadSingleArticleCmd user =
    Browser.console.log("Load next article to be annotated")
    Cmd.ofPromise loadArticles (user, NextArticle) FetchedNextArticle FetchError

let loadUnfinishedArticleCmd user =
    Browser.console.log("Load next article to be annotated")
    Cmd.ofPromise loadArticles (user, Unfinished) FetchedUnfinishedArticle FetchError    

let userPassedTraining user =
    promise {
        let url = ServerUrls.APIUrls.User
        let body = toJson (user)
        let props =
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + user.Token)
                HttpRequestHeaders.ContentType "application/json" ]
              RequestProperties.Body !^body ]

        return! Fetch.fetchAs<bool> url props
    }


let userPassedTrainingCmd user =
    Browser.console.log("User passed training batch of articles")
    Cmd.ofPromise userPassedTraining user UserPassedTraining StorageFailure


let init result =
    let user = loadUser ()
    let stateJson: string option = !!Browser.window?__INIT_MODEL__
    match stateJson, result with
    | Some json, Some Page.Home ->
        Browser.console.log("Loading previous model")
        let model: Model = ofJson json
        { model with User = user }, 
        Cmd.none

    | _ ->
        let model =
            { User = user
              SelectedArticle = None
              ArticleToAnnotate = None
              AllArticles = None
              PageModel = HomePageModel }

        urlUpdate result model

let update msg model =
    match msg, model.PageModel with
    | StorageFailure e, _ ->
        printfn "Unable to access local storage: %A" e
        model, Cmd.none

    | LoginMsg msg, LoginModel m ->
        let m, cmd, externalMsg = Login.update msg m

        let cmd2 =
            match externalMsg with
            | Login.ExternalMsg.NoOp ->
                Cmd.none
            | Login.ExternalMsg.UserLoggedIn newUser ->
                saveUserCmd newUser

        { model with
            PageModel = LoginModel m },
                Cmd.batch [
                    Cmd.map LoginMsg cmd
                    cmd2 ]

    | LoginMsg _, _ -> model, Cmd.none

    | AnnotationsMsg msg, AnnotationsModel m ->
        let m, cmd, externalMsg = Annotations.update msg m

        match externalMsg with
        | Annotations.ExternalMsg.NoOp -> 
            { model with
                PageModel = AnnotationsModel m }, 
                Cmd.map AnnotationsMsg cmd         

        | Annotations.ExternalMsg.ViewArticle a ->
            { model with
                PageModel = AnnotationsModel m
                SelectedArticle = Some a
             }, 
            Cmd.batch [
                    Cmd.map AnnotationsMsg cmd 
                    Navigation.newUrl (toPath Page.Article) 
                    ]

        | Annotations.ExternalMsg.GetAllArticles ->
            { model with 
                PageModel = AnnotationsModel m }, 
            Cmd.batch [
                Cmd.map AnnotationsMsg cmd
                loadArticlesCmd model.User.Value PreviouslyAnnotated
                loadUnfinishedArticleCmd model.User.Value
            ]

        | Annotations.ExternalMsg.GetNextArticle ->
            { model with 
                PageModel = AnnotationsModel m }, 
            Cmd.batch [
                Cmd.map AnnotationsMsg cmd
                loadSingleArticleCmd model.User.Value
            ]

    | AnnotationsMsg _, _ ->
        model, Cmd.none

    | FetchedArticles annotations, _ ->
        Browser.console.log("Fetched previously annotated articles from database")
        let annotations = 
            { annotations with Articles = annotations.Articles  }
        
        { model with 
            AllArticles = Some annotations
            }  ,
        Cmd.batch [
            Cmd.map AnnotationsMsg (Cmd.ofMsg (Annotations.FetchedArticles annotations)) ]

    | FetchedNextArticle article, _ ->
        Browser.console.log("Fetched next article to be annotated")

        if article.Articles.Length > 0 then
            let article', _ = article.Articles |> List.exactlyOne

            let m, cmd = Article.init model.User.Value article'
            { model with 
                PageModel = ArticleModel m
                ArticleToAnnotate = Some article'; 
                SelectedArticle = Some article'
                } , 
            Cmd.batch [
                Navigation.newUrl (toPath Page.Article)
                Cmd.map ArticleMsg cmd 
            ]
        else
            // no articles returned
            // possible causes:
            // - Finished training
            // - No articles left
            // - Error

            if model.User.IsSome && 
                (model.User.Value.Proficiency = Training("Expert") ||
                 model.User.Value.Proficiency = Training("User")) then
                // user finished training batch

                model, 
                userPassedTrainingCmd model.User.Value
            else
                Browser.console.log("No articles to show") 

                let m, cmd = Annotations.init(model.User.Value, model.AllArticles, model.ArticleToAnnotate)
                
                { model with PageModel = AnnotationsModel m }, 
                Cmd.batch [
                    Cmd.map AnnotationsMsg cmd
                    Cmd.map AnnotationsMsg (Cmd.ofMsg (Annotations.NoArticlesFound))
                ]
                

    | UserPassedTraining(success), _ ->
        if success then 
            let proficiency = 
                match model.User.Value.Proficiency with
                | Training(x) ->
                    match x with
                    | "User" -> User
                    | "Expert" -> Expert
                    | _ -> User
                | _ -> User

            let newUserValue = {model.User.Value with Proficiency = proficiency}
            { model with
                    User = Some(newUserValue)
                    }, 
            loadSingleArticleCmd newUserValue
        else
            Browser.console.log("Cannot mark user as 'passed training'")
            model, Cmd.none

    | FetchedUnfinishedArticle articles, _ ->
        Browser.console.log("Fetched unfinished article from database")
        if articles.Articles.IsEmpty then
            Browser.console.log("No unfinished articles found")

            { model with
                ArticleToAnnotate = None
                 }  ,
            Cmd.none
        else
            Browser.console.log("Forwarding unfinished article")
            let article = articles.Articles |> List.head |> fst

            { model with 
                ArticleToAnnotate = Some article
                }  ,
            Cmd.batch [
                Cmd.map AnnotationsMsg (Cmd.ofMsg (Annotations.FetchedUnfinishedArticle articles))
            ]

        

    | FetchError e,_ ->
        Browser.console.log(e.Message)
        Browser.console.log(e)
        Browser.console.log(model)

        model, Cmd.none


    | LoggedIn newUser, _ ->
        let nextPage = Page.Annotations
        { model with User = Some newUser },
        Navigation.newUrl (toPath nextPage)

    | LoggedOut, _ ->
        { model with
            User = None
            PageModel = HomePageModel },
        Navigation.newUrl (toPath Page.Home)

    | Logout(), _ ->
        model, deleteUserCmd

    | ArticleMsg msg, ArticleModel m ->
        let m', cmd, externalMsg = Article.update msg m

        match externalMsg with
        | Article.ExternalMsg.NoOp ->
            { model with PageModel = ArticleModel m' }, Cmd.map ArticleMsg cmd

        | Article.ExternalMsg.MarkAsAnnotated id ->
            // a) Newly annotated article is a new article 
            // b) Newly annotated article is an edit of an older article

            match model.ArticleToAnnotate with
            | Some a -> 
                if a.ID = id then 
                    let model' = 
                      { model with 
                          AllArticles = 
                            // new article was annotated
                            model.AllArticles
                            |> Option.map (fun aa -> 
                                { aa with Articles = (a, true)::aa.Articles })
                          ArticleToAnnotate = None }
                    { model' with PageModel = ArticleModel m' }, 
                    Cmd.map ArticleMsg cmd
                else
                    // the annotated article is an older article - no need to do anything
                    // because the article is already marked as annotated
                    { model with PageModel = ArticleModel m' },
                    Cmd.map ArticleMsg cmd

            | None ->
                // the annotated article is an older article - no need to do anything
                // because the article is already marked as annotated
                { model with PageModel = ArticleModel m' },
                Cmd.map ArticleMsg cmd


        | Article.ExternalMsg.GetNextArticle ->
            match model.ArticleToAnnotate with
            | None -> 
                { model with    
                    PageModel = ArticleModel m' }, 
                Cmd.batch [
                    Cmd.map ArticleMsg cmd
                    loadSingleArticleCmd model.User.Value
                ]
            | Some(article) ->
                let m'', cmd' = Article.init model.User.Value article

                { model with 
                    PageModel = ArticleModel m''
                    SelectedArticle = Some article
                    } , 
                Cmd.batch [
                    Navigation.newUrl (toPath Page.Article)
                    Cmd.map ArticleMsg cmd' 
                ] 


    | ArticleMsg msg, _ ->
        model, Cmd.none

open Elmish.Debug

let withReact =
    if (!!Browser.window?__INIT_MODEL__)
    then Program.withReactHydrate
    else Program.withReact


// App
Program.mkProgram init update view
|> Program.toNavigable Pages.urlParser urlUpdate
#if DEBUG
|> Program.withConsoleTrace
|> Program.withHMR
#endif
|> withReact "elmish-app"
//#if DEBUG
//|> Program.withDebugger
//#endif
|> Program.run
