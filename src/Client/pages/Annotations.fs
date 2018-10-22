module Client.Annotations

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

type Model =
  { Annotations : ArticleList
    Token : string
    UserName : string
    ResetTime : DateTime option
    ErrorMsg : string option
    SelectedArticle : Article option }

/// The different messages processed when interacting with the wish list
type Msg =
    | LoadForUser of string
    | FetchedAnnotations of ArticleList
    | FetchedResetTime of DateTime
    | FetchError of exn
    | SelectArticle of Article

type ExternalMsg =
    | ViewArticle of Article
    | NoOp
    | CacheAllArticles of ArticleList

let loadAnnotations (user: UserData) =
    promise {
        let url = ServerUrls.APIUrls.Annotations
        let body = toJson user
        let props =
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + user.Token)
                HttpRequestHeaders.ContentType "application/json" ]
              RequestProperties.Body !^body ]

        return! Fetch.fetchAs<ArticleList> url props
    }

let loadAnnotationsCmd user =
    Browser.console.log("Requesting articles")
    Cmd.ofPromise loadAnnotations user FetchedAnnotations FetchError

let getResetTime token =
    promise {
        let url = ServerUrls.APIUrls.ResetTime
        let props =
            [ Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + token) ]]

        let! details = Fetch.fetchAs<ServerCode.Domain.AnnotationsResetDetails> url props
        return details.Time
    }

let loadResetTimeCmd token =
    Cmd.ofPromise getResetTime token FetchedResetTime FetchError


let init (user:UserData, articleList : ArticleList option) =
    Browser.console.log("Initializing list of annotations")
    { Annotations = 
        match articleList with
        | None -> ArticleList.New user.UserName
        | Some a -> a
      Token = user.Token
      UserName = user.UserName
      ResetTime = None
      ErrorMsg = None
      SelectedArticle = None
      },
      match articleList with 
      | None -> loadAnnotationsCmd user
      | Some _ -> Cmd.none

let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | LoadForUser user ->
        model, Cmd.none, NoOp

    | FetchedAnnotations annotations ->
        Browser.console.log("Fetched annotations - adapting model")
        Browser.console.log(annotations)
        let annotations = 
            { annotations with Articles = annotations.Articles  }
        { model with Annotations = annotations }, Cmd.none, CacheAllArticles annotations

    | FetchedResetTime datetime ->
        { model with ResetTime = Some datetime }, Cmd.none, NoOp

    | FetchError e ->
        Browser.console.log(e.Message)
        Browser.console.log(e)
        Browser.console.log(model)
        { model with ErrorMsg = Some e.Message }, Cmd.none, NoOp
       
    | SelectArticle a ->
        { model with SelectedArticle = Some a }, Cmd.none, ViewArticle a


let viewArticleComponent article annotated (dispatch: Msg -> unit) =
  tr [ OnClick (fun _ -> dispatch (SelectArticle article)) 
       ClassName (if annotated then "annotated" else "to-annotate")] [
    td [] [
            yield buttonLink "" (fun _ -> dispatch (SelectArticle article)) [ str article.Title ] 
        ]
    td [] [
        if annotated then
           yield str "Submitted"
        else  
           yield str "Annotate" 
      ]
    ]

let view (model:Model) (dispatch: Msg -> unit) =
    div [] [
        h4 [] [
            let time = model.ResetTime |> Option.map (fun t -> " - Last database reset at " + t.ToString("yyyy-MM-dd HH:mm") + "UTC") |> Option.defaultValue ""
            yield str (sprintf "Annotations for %s%s" model.Annotations.UserName time) ]
        table [ClassName "table table-striped table-hover"] [
            thead [] [
                    tr [] [
                        th [] [str "Title"]
                        th [] [str ""]
                ]
            ]
            tbody [] (
                model.Annotations.Articles
                |> List.map(fun (article, annotated) ->
                    viewArticleComponent article annotated dispatch
                    )
                //
            )
        ]
    ]
