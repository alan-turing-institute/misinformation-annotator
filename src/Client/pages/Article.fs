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

type Selection = {
    StartParagraphIdx: int
    EndParagraphIdx : int
    StartIdx : int  // within parent paragraph
    EndIdx: int     // within parent paragraph
    Text: string
}

type Model = {
    Heading: string
    Text: string []
    Link: string
    Tags: string list
}

type Msg = 
    | View
    | FetchedArticle of Article
    | FetchError of exn
    | FetchArticle
    | TextSelected of Selection option

type ExternalMsg = 
    | DisplayArticle of Article
    | NoOp    

(*
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
*)


let postArticle (article : Domain.Article) =
    promise {
        let url = ServerUrls.APIUrls.Article
        let body = toJson article
        let props = 
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
//                HttpRequestHeaders.Authorization ("Bearer " + token)
                HttpRequestHeaders.ContentType "application/json" ]
              RequestProperties.Body !^body ]
        return! Fetch.fetchAs<Article> url props          
    }

let postArticleCmd article = 
    Cmd.ofPromise postArticle article FetchedArticle FetchError

let init (user:UserData) (article: Article)  = 
    { Heading = article.Title
      Text = match article.Text with | Some t -> t | None -> [||]
      Tags = []
      Link = article.Link }, 
    postArticleCmd article 

[<Emit("window.getSelection()")>]
let jsGetSelection () : obj = jsNative    

[<Emit("$0.toString()")>]
let jsExtractText (selection: obj) : string = jsNative

let getSelection () = 
    let rawOutput = jsGetSelection()
    match string (rawOutput?("type")) with
    | "Range" -> 
        let startParagraph = rawOutput?anchorNode?parentElement?id |> unbox<int>
        let endParagraph = rawOutput?focusNode?parentElement?id |> unbox<int>
        let startIdx = rawOutput?anchorOffset |> unbox<int>
        let endIdx = rawOutput?focusOffset |> unbox<int>
        let startP, startI, endP, endI =
            if startParagraph < endParagraph then
                startParagraph, startIdx, endParagraph, endIdx
            else if startParagraph = endParagraph then
                startParagraph, min startIdx endIdx, endParagraph, max startIdx endIdx
            else 
                endParagraph, endIdx, startParagraph, startIdx

        { StartParagraphIdx = startP
          StartIdx = startI
          EndParagraphIdx = endP
          EndIdx = endI
          Text = jsExtractText(rawOutput)} |> Some
          
    | _ -> None

let view (model:Model) (dispatch: Msg -> unit) =
    [
        div [ ClassName "container" ] [
            yield button [ ClassName "btn" ] [ str "Tag 1"]
            yield button [ ClassName "btn btn-success" ] [ str "Tag 2"]
            yield button [ ClassName "btn"] [str "Tag 3"]
            yield h1 [] [ str (model.Heading) ]
            yield div [ ] [
                for idx, paragraph in (Array.zip [|1..model.Text.Length|] model.Text) do
                    yield p [ OnMouseUp (fun _ -> dispatch (TextSelected (getSelection()))) 
                              Id (string idx) ] [ str paragraph ]
            ]
        ]
    ]


 
let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | FetchArticle -> 
        model, postArticleCmd { Article.Title = model.Heading; Link = model.Link; Text = None }, NoOp
        
    | View -> 
        Browser.console.log("View message in update")
        model, Cmd.none, NoOp //DisplayArticle model.Article

    | TextSelected (t:Selection option) ->
        Browser.console.log("Text selected:")
        Browser.console.log(t)
        model, Cmd.none, NoOp

    | FetchedArticle a ->
        Browser.console.log("Fetched article!")
        
        match a.Text with
        | Some t ->         
            Browser.console.log(t.[0])
            Browser.console.log("Trying to add article text to the model")
            { model with Text = t }, Cmd.none, NoOp
        | None -> model, Cmd.none, NoOp

    | FetchError e ->
        model, Cmd.none, NoOp

