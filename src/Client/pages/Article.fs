module Client.Article

open Fable.Core
open Fable.Import
open Fable.PowerPack
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.Core.JsInterop

open Elmish
open Fetch.Fetch_types
open ServerCode
open ServerCode.Domain
open Style
open System

type Model = {
    Article: Article option
    Tags: string list
}

type Msg = 
    | View
    | FetchedArticle of Article
    | FetchError of exn

type ExternalMsg = 
    | DisplayArticle of Article
    | NoOp    

let getArticle token =
    promise {
        let url = ServerUrls.APIUrls.Article
        let props =
            [ Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + token) ]]

        return! Fetch.fetchAs<Article> url props
    }


let loadArticleCmd token =
    Cmd.ofPromise getArticle token FetchedArticle FetchError

let init (user:UserData) (article: Article)  = 
    { Article = Some article; Tags = [] }, 
    loadArticleCmd user.Token

let view (model:Model) (dispatch: Msg -> unit) =
    match model.Article with
    | Some article ->
        [
            div [ ClassName "article" ] [
                yield h1 [] [ str (article.Title) ]
                match article.Text with
                | Some text ->
                    for paragraph in text do
                        yield p [] [ str paragraph ]
                | None ->
                    yield p [] [ str "Text not loaded." ]
            ]
        ]
 
    | None -> 
        [
            div [ ClassName "article" ] [ 
                h2 [] [ str "No article selected."] 
            ]
        ]

let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | View -> 
        match model.Article with 
        | Some a -> model, Cmd.none, DisplayArticle a
        | None -> model, Cmd.none, NoOp
    | FetchedArticle a ->
        { model with Article = Some a }, Cmd.none, NoOp
    | FetchError e ->
        model, Cmd.none, NoOp

