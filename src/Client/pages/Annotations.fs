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
  { PreviouslyAnnotated : ArticleList
    CurrentArticle : Article option    // Currently assigned article for annotation
    Token : string
    UserInfo : UserData
    ResetTime : DateTime option
    ErrorMsg : string option
    SelectedArticle : Article option   // Article clicked on
    Finished : bool
    Loading : bool }

/// The different messages processed when interacting with the wish list
type Msg =
    | LoadForUser of string
    | FetchedArticles of ArticleList
    | FetchedNextArticle of ArticleList
    | FetchedResetTime of DateTime
    | FetchError of exn
    | SelectArticle of Article
    | LoadArticleBatch of ArticleAssignment
    | LoadSingleArticle

type ExternalMsg =
    | ViewArticle of Article
    | NoOp
    | CacheAllArticles of ArticleList

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
    Browser.console.log("Load next article to annotated")
    Cmd.ofPromise loadArticles (user, NextArticle) FetchedNextArticle FetchError

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

let checkIfFinished model = 
    { model with 
        Finished = 
            (true, model.PreviouslyAnnotated.Articles)
            ||> List.fold (fun state (_, annotated) -> state && annotated)
    }

let init (user:UserData, articleList : ArticleList option) =
    Browser.console.log("Initializing list of annotations")
    { PreviouslyAnnotated = 
        match articleList with
        | None -> ArticleList.New user.UserName
        | Some a -> a
      Token = user.Token
      UserInfo = user
      ResetTime = None
      ErrorMsg = None
      SelectedArticle = None
      CurrentArticle = None
      Finished = false
      Loading = 
        match articleList with
        | None -> true
        | Some _ -> false
    } |> checkIfFinished,
      match articleList with 
      | None -> 
        Cmd.batch [
            loadArticlesCmd user PreviouslyAnnotated
            loadSingleArticleCmd user
        ]
      | Some _ -> Cmd.none

let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | LoadForUser user ->
        model |> checkIfFinished, Cmd.none, NoOp

    | FetchedArticles annotations ->
        Browser.console.log("Fetched annotations - adapting model")
        Browser.console.log(annotations)
        let annotations = 
            { annotations with Articles = annotations.Articles  }
        { model with PreviouslyAnnotated = annotations; Loading = false } |> checkIfFinished, Cmd.none, CacheAllArticles annotations

    | FetchedNextArticle article ->
        Browser.console.log("Fetched next article to annotate")
        Browser.console.log(article)
        let article', _ = article.Articles |> List.exactlyOne
        { model with CurrentArticle = Some article'; Loading = false } |> checkIfFinished, 
        Cmd.none, NoOp


    | FetchedResetTime datetime ->
        { model with ResetTime = Some datetime } |> checkIfFinished, Cmd.none, NoOp

    | FetchError e ->
        Browser.console.log(e.Message)
        Browser.console.log(e)
        Browser.console.log(model)
        { model with ErrorMsg = Some e.Message } |> checkIfFinished, Cmd.none, NoOp
       
    | SelectArticle a ->
        { model with SelectedArticle = Some a } |> checkIfFinished, Cmd.none, ViewArticle a

    | LoadArticleBatch articleType ->
        { model with Loading = true } |> checkIfFinished, loadArticlesCmd model.UserInfo articleType, NoOp

    | LoadSingleArticle ->
        model, 
        loadSingleArticleCmd model.UserInfo,
        NoOp


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
            yield str (sprintf "Annotations for %s%s" model.PreviouslyAnnotated.UserName time) ]

        (if model.Loading then
           div [] [
             div [ ClassName "body" ] [
                 img [ HTMLAttr.Src "Images/Double Ring-1.7s-200px.gif" ] 
             ]
//            h5 [] [ str "Loading articles..."]
             div [ ClassName "bottom" ] [ a [ HTMLAttr.Href "https://loading.io/spinner/double-rignt/"] [ str "Spinner by loading.io"] ]
           ]
         else
            // check if there are any unfinished articles or if all work has been finished
            div [] [
                ( if model.PreviouslyAnnotated.Articles.Length > 0 then     
                    // Existing articles to annotate
                      div [] [
                        table [ClassName "table table-striped table-hover"] [
                            thead [] [
                                    tr [] [
                                        th [] [str "Title"]
                                        th [] [str ""]
                                ]
                            ]
                            tbody [] (
                                model.PreviouslyAnnotated.Articles
                                |> List.map(fun (article, annotated) ->
                                    viewArticleComponent article annotated dispatch
                                    )
                                //
                            )
                        ]
                    ]
                  else  div [][]
                ); 

                (if model.Finished then
                    // load new articles to annotate 
                    div [] [
                        button 
                          [ ClassName "bnt btn-light" 
                            OnClick (fun _ -> dispatch (LoadSingleArticle)) ]
                          [ str "Next article to annotate" ]
                    ]
                 else div [] []
                )
            ]
               
        )
    ]
