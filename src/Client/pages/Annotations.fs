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

/// Get the wish list from the server, used to populate the model
let getAnnotations token =
    promise {
        let url = ServerUrls.APIUrls.Annotations
        let props =
            [ Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + token) ]]

        return! Fetch.fetchAs<ArticleList> url props
    }

let getResetTime token =
    promise {
        let url = ServerUrls.APIUrls.ResetTime
        let props =
            [ Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + token) ]]

        let! details = Fetch.fetchAs<ServerCode.Domain.AnnotationsResetDetails> url props
        return details.Time
    }

let loadAnnotationsCmd token =
    Browser.console.log("Requesting articles")
    Cmd.ofPromise getAnnotations token FetchedAnnotations FetchError

let loadResetTimeCmd token =
    Cmd.ofPromise getResetTime token FetchedResetTime FetchError


let postAnnotations (token,annotations) =
    promise {
        let url = ServerUrls.APIUrls.Annotations
        let body = toJson annotations
        let props =
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + token)
                HttpRequestHeaders.ContentType "application/json" ]
              RequestProperties.Body !^body ]

        return! Fetch.fetchAs<ArticleList> url props
    }

let postAnnotationsCmd (token,annotations) =
    Cmd.ofPromise postAnnotations (token,annotations) FetchedAnnotations FetchError


let init (user:UserData) =
    Browser.console.log("Initializing list of annotations")
    { Annotations = ArticleList.New user.UserName
      Token = user.Token
      UserName = user.UserName
      ResetTime = None
      ErrorMsg = None
      SelectedArticle = None },
        Cmd.batch [
            loadAnnotationsCmd user.Token
            loadResetTimeCmd user.Token ]

let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | LoadForUser user ->
        model, Cmd.none, NoOp

    | FetchedAnnotations annotations ->
        Browser.console.log("Fetched annotations - adapting model")
        Browser.console.log(annotations)
        let annotations = 
            { annotations with Articles = annotations.Articles |> List.sortBy (fun (article, anns) -> article.Title) }
        { model with Annotations = annotations }, Cmd.none, NoOp

    | FetchedResetTime datetime ->
        { model with ResetTime = Some datetime }, Cmd.none, NoOp

    | FetchError e ->
        Browser.console.log(e.Message)
        Browser.console.log(e)
        Browser.console.log(model)
        { model with ErrorMsg = Some e.Message }, Cmd.none, NoOp
       
    | SelectArticle a ->
        { model with SelectedArticle = Some a }, Cmd.none, ViewArticle a


type [<Pojo>] ArticleProps = { key: string; article: Article; viewArticle: unit -> unit; annotated : bool }

let articleComponent { article = article; viewArticle = viewArticle; annotated = annotated } =
  tr [] [
    td [] [
        if String.IsNullOrWhiteSpace article.ID then
            yield str article.Title
        else
            yield str article.Title 
        ]
    td [] [
        if annotated then
           yield buttonLink "" viewArticle [ str "✅ View"] 
        else  
           yield buttonLink "" viewArticle  [ str "Annotate" ] 
      ]
    ]

let inline ArticleComponent props = (ofFunction articleComponent) props []

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
            tbody [] [
                model.Annotations.Articles
                    |> List.map(fun (article, annotated) ->
                        ArticleComponent {
                            key = article.Title
                            article = article
                            viewArticle = (fun _ -> dispatch (SelectArticle article))
                            annotated = annotated
                        })
                    |> ofList
            ]
        ]
    ]
