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
            let m, cmd = Annotations.init user
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
                let m, cmd = Article.init article
                { model with PageModel = ArticleModel m }, Cmd.map ArticleMsg cmd
            | None ->
                model, Cmd.none
        | None -> 
            model, Cmd.ofMsg (Logout())

let loadUser () : UserData option =
    BrowserLocalStorage.load "user"

let saveUserCmd user =
    Cmd.ofFunc (BrowserLocalStorage.save "user") user (fun _ -> LoggedIn user) StorageFailure

let deleteUserCmd =
    Cmd.ofFunc BrowserLocalStorage.delete "user" (fun _ -> LoggedOut) StorageFailure

let selectArticle article =
    Cmd.ofFunc (BrowserLocalStorage.save "article") article (fun _ -> SelectedArticle article) StorageFailure


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
                    selectArticle a
                    Navigation.newUrl (toPath Page.Article) ]

    | AnnotationsMsg _, _ ->
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
        let cmd' =
            match externalMsg with
            | Article.ExternalMsg.DisplayArticle a -> selectArticle a
            | Article.ExternalMsg.NoOp -> Cmd.none
        model, cmd'

    | ArticleMsg msg, _ ->
        model, Cmd.none

    | SelectedArticle a, ArticleModel m ->
        { model with SelectedArticle = Some a },
        Navigation.newUrl (toPath Page.Article)

    | SelectedArticle a, _ ->
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
#if DEBUG
|> Program.withDebugger
#endif
|> Program.run
