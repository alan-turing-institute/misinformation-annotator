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
  { Annotations : Annotations
    Token : string
    UserName : string
    ResetTime : DateTime option
    ErrorMsg : string option
    SelectedArticle : Article option }

/// The different messages processed when interacting with the wish list
type Msg =
    | LoadForUser of string
    | FetchedAnnotations of Annotations
    | FetchedResetTime of DateTime
    | FetchError of exn
    | SelectArticle of Article


/// Get the wish list from the server, used to populate the model
let getAnnotations token =
    promise {
        let url = ServerUrls.APIUrls.Annotations
        let props =
            [ Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + token) ]]

        return! Fetch.fetchAs<Annotations> url props
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

        return! Fetch.fetchAs<Annotations> url props
    }

let postAnnotationsCmd (token,annotations) =
    Cmd.ofPromise postAnnotations (token,annotations) FetchedAnnotations FetchError


let init (user:UserData) =
    { Annotations = Annotations.New user.UserName
      Token = user.Token
      UserName = user.UserName
      ResetTime = None
      ErrorMsg = None
      SelectedArticle = None },
        Cmd.batch [
            loadAnnotationsCmd user.Token
            loadResetTimeCmd user.Token ]

let update (msg:Msg) model : Model*Cmd<Msg> =
    match msg with
    | LoadForUser user ->
        model, Cmd.none

    | FetchedAnnotations annotations ->
        let annotations = { annotations with Articles = annotations.Articles |> List.sortBy (fun b -> b.Title) }
        { model with Annotations = annotations }, Cmd.none

    | FetchedResetTime datetime ->
        { model with ResetTime = Some datetime }, Cmd.none

    | FetchError e ->
        { model with ErrorMsg = Some e.Message }, Cmd.none
       
    | SelectArticle a ->
        // TODO
        model, Cmd.none


type [<Pojo>] ArticleProps = { key: string; article: Article; viewArticle: unit -> unit }

let articleComponent { article = article; viewArticle = viewArticle } =
  tr [] [
    td [] [
        if String.IsNullOrWhiteSpace article.Link then
            yield str article.Title
        else
            yield str article.Title 
        ]
    td [] [
           td [] [ buttonLink "" viewArticle  [ str "Annotate" ] ]
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
                        th [] [str "Annotate!"]
                ]
            ]
            tbody [] [
                model.Annotations.Articles
                    |> List.map(fun article ->
                        ArticleComponent {
                            key = article.Title
                            article = article
                            viewArticle = (fun _ -> dispatch (SelectArticle article))
                        })
                    |> ofList
            ]
        ]
    ]
