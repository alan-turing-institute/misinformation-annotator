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




let init result =
    let user = loadUser ()
    let stateJson: string option = !!Browser.window?__INIT_MODEL__
    match stateJson, result with
    | Some json, Some Page.Home ->
        let model: Model = ofJson json
        { model with User = user }, Cmd.none
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
            ]

        | Annotations.ExternalMsg.GetNextArticle ->
            { model with 
                PageModel = AnnotationsModel m }, 
            Cmd.batch [
                Cmd.map AnnotationsMsg cmd
                loadSingleArticleCmd model.User.Value
            ]
        
        | Annotations.ExternalMsg.GetUnannotated ->
            { model with 
                PageModel = AnnotationsModel m }, 
            Cmd.batch [
                Cmd.map AnnotationsMsg cmd
                loadSingleArticleCmd model.User.Value 
            ]

    | AnnotationsMsg _, _ ->
        model, Cmd.none

    | FetchedArticles annotations, _ ->
        let annotations = 
            { annotations with Articles = annotations.Articles  }
        
        let m, cmd = Annotations.init(model.User.Value, Some annotations, model.ArticleToAnnotate)
        { model with 
            AllArticles = Some annotations
            PageModel = AnnotationsModel m }  ,
        Cmd.batch [
            Navigation.newUrl (toPath Page.Annotations)        
            Cmd.map AnnotationsMsg cmd ]

    | FetchedNextArticle article, _ ->
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

    | FetchedUnfinishedArticle articles, _ ->
        if articles.Articles.Length = 1 then
            let article = articles.Articles |> List.exactlyOne |> fst
            let m, cmd = Annotations.init(model.User.Value, model.AllArticles, Some article)

            { model with 
                ArticleToAnnotate = Some article
                PageModel = AnnotationsModel m }  ,
            Cmd.batch [
                Navigation.newUrl (toPath Page.Annotations)
                Cmd.map AnnotationsMsg cmd 
            ]
        else
            let m, cmd = Annotations.init(model.User.Value, model.AllArticles, None)

            { model with
                ArticleToAnnotate = None
                PageModel = AnnotationsModel m
                 }  ,
            Cmd.batch [             
                Navigation.newUrl (toPath Page.Annotations)
                Cmd.map AnnotationsMsg cmd ]
        

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
            // TODO
            let model' = 
                { model with 
                   AllArticles = 
                    model.AllArticles 
                    |> Option.map (fun aa ->
                        { aa with 
                            Articles = 
                              aa.Articles
                              |> List.map (fun (a, isAnnotated) -> 
                                  if a.ID = id then (a, true) else (a, isAnnotated))})}
            { model' with PageModel = ArticleModel m' }, 
            Cmd.map ArticleMsg cmd

        | Article.ExternalMsg.GetNextArticle id ->
            // TODO: change this
            Browser.console.log("Going to the next article...")
            // mark current article as annotated 
            let model' = 
                { model with 
                   AllArticles = 
                    model.AllArticles 
                    |> Option.map (fun aa ->
                        { aa with 
                            Articles = 
                              aa.Articles
                              |> List.map (fun (a, isAnnotated) -> 
                                  if a.ID = id then (a, true) else (a, isAnnotated))})}

            // TODO: Change this - Fetch next article!
            // find the next article
            let nextIdx = 
                match model.AllArticles with
                | None -> None
                | Some allArticles ->
                    allArticles.Articles
                    |> List.findIndex (fun (a,_) -> a.ID = id)
                    |> fun i -> if i+1 = allArticles.Articles.Length then None else Some(i+1)

            // Create next article model
            match nextIdx with
            | Some nextIdx' ->
                let m'', cmd' = Article.init m'.User (model.AllArticles.Value.Articles.[nextIdx'] |> fst)

                { model' with
                    PageModel = ArticleModel m''
                    SelectedArticle = Some (model.AllArticles.Value.Articles.[nextIdx'] |> fst)
                 }, 
                Cmd.batch [
                        Cmd.map ArticleMsg cmd'
                        Navigation.newUrl (toPath Page.Article) 
                        ]

            | None ->    
                let m, cmd = Annotations.init(model'.User.Value, model.AllArticles, model.ArticleToAnnotate)
                { model' with 
                    PageModel = AnnotationsModel m },
                Cmd.batch [
                    Cmd.map AnnotationsMsg cmd
                    Navigation.newUrl (toPath Page.Annotations)
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
