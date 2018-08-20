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
    Heading: string
    Text: string [] 
    Link: string
    Tags: string list
    Q0_MentionsSources: bool option
    SourceInfo : SourceInfo []
    SourceSelectionMode : int option // id of the source that's being currently annotated
}

type Msg = 
    | View
    | FetchedArticle of Article
    | FetchError of exn
    | FetchArticle
    | TextSelected of (int*Selection) option
    | Q0_MentionsSources of bool
    | HighlightSource of int
    | FinishedHighlighting
    | ClearHighlights of int
    | AddSource of int

type ExternalMsg = 
    | DisplayArticle of Article
    | NoOp    


let fetchArticle (article : Domain.Article) =
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

let fetchArticleCmd article = 
    Cmd.ofPromise fetchArticle article FetchedArticle FetchError


(*
let postArticleAnnotations (model : Model) =
    promise {
        let url = ServerUrls.APIUrls.Article
        let body = toJson model.SourceInfo // todo - what information to actually post? New type for annotations
        let props =             
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
                //HttpRequestHeaders.Authorization ("Bearer " + token)
                HttpRequestHeaders.ContentType "application/json" ]
                RequestProperties.Body !^body ]
        return! Fetch.fetchAs<Annotations> url props
    }
*)

let postAnswers (model: Model) = 
    promise {
        let url = ServerUrls.APIUrls.Answers
        let body = toJson model.SourceInfo
        let! response = 
            Fetch.postRecord url { 
                Title = model.Heading; 
                ID = model.Link; 
                Annotations = model.SourceInfo } []
        let! resp = response.json<AnswersResponse>()
        
        return resp }


let init (user:UserData) (article: Article)  = 
    { Heading = article.Title
      Text = match article.Text with | Some t -> t | None -> [||]
      Tags = []
      Link = article.ID 
      Q0_MentionsSources = None 
      SourceInfo = [||]
      SourceSelectionMode = None }, 
    fetchArticleCmd article 

[<Emit("window.getSelection()")>]
let jsGetSelection () : obj = jsNative    

[<Emit("$0.toString()")>]
let jsExtractText (selection: obj) : string = jsNative

[<Emit("window.getSelection().removeAllRanges()")>]
let jsRemoveSelection() = jsNative

let getSelection (model: Model) = 
    Browser.console.log(jsGetSelection())
    match model.SourceSelectionMode with
    | None -> None
    | Some id ->
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

            jsRemoveSelection()
            (id, 
             { StartParagraphIdx = startP
               StartIdx = startI
               EndParagraphIdx = endP
               EndIdx = endI
               Text = jsExtractText(rawOutput)}) |> Some
        | _ -> None
    
let viewAddSource (model: Model) n (dispatch: Msg -> unit) =
    div [ClassName "container"] [
        h4 [ ClassName ("question" + string n) ] [ str ("Source number " + string (n+1)) ]
        ol [ ] [
            li [ ] 
               [ str "Highlight the portion of the text where you find the source."
                 br []
                 str "There may be multiple sections in the text that refer to the same source. Click Start to start highlighting, and Finish to complete highlighting."
                 br []
                 button [ OnClick (fun _ -> dispatch (HighlightSource n))
                          (match model.SourceSelectionMode with
                           | Some i -> ClassName "btn btn-disabled" 
                           | None -> ClassName "btn btn-primary") ] 
                          [ str "Start" ]
                 button [ OnClick (fun _ -> dispatch (FinishedHighlighting))
                          (match model.SourceSelectionMode with
                           | Some i -> if i = n then ClassName "btn btn-primary" else ClassName "btn btn-disabled"
                           | None -> ClassName "btn btn-disabled") ] 
                          [ str "Finish" ]
                 button [ OnClick (fun _ -> dispatch (ClearHighlights n)) 
                          ClassName "btn btn-secondary" ] 
                          [ str "Clear highlights" ] ]
        ]
    ]

type ParagraphPart = {
    StartIdx : int
    EndIdx : int 
    SpanId : int option
    Text : string
}

let viewParagraphHighlights (model: Model) paragraphIdx (text: string)  =
    if model.SourceInfo.Length = 0 then [ str text ] else

    // split up into pieces first, add span attributes later
    let initialState = [{StartIdx = 0; EndIdx=text.Length-1; SpanId = None; Text = text }]
    let updatedParts = 
        (initialState, model.SourceInfo)
        ||> Array.fold (fun paragraphParts sourceInfo ->
            // selected specific source, loop over highlights
            let allSelections = sourceInfo.TextMentions
            (paragraphParts, allSelections) 
            ||> List.fold (fun paragraphParts' selection -> 
                if paragraphIdx >= selection.StartParagraphIdx && paragraphIdx <= selection.EndParagraphIdx then
                    let startI = 
                        if selection.StartParagraphIdx = paragraphIdx then selection.StartIdx
                        else 0
                    let endI = 
                        if selection.EndParagraphIdx = paragraphIdx then selection.EndIdx
                        else text.Length       
                    
                    // Loop over paragraph parts
                    paragraphParts'
                    |> List.collect (fun part -> 
                            let idx i = i - part.StartIdx   
                            if (part.StartIdx > endI) || (part.EndIdx < startI) then [part] else
                            if part.StartIdx >= startI && part.EndIdx <= endI-1 then
                                [ { part with SpanId = Some sourceInfo.Id } ] 
                            else if part.StartIdx <= startI && part.EndIdx >= endI-1 then
                                let text1,text2,text3 = 
                                    part.Text.[0.. idx startI-1], 
                                    part.Text.[idx startI .. idx endI-1], 
                                    part.Text.[idx endI ..]
                                [   
                                    if text1 <> "" then 
                                        yield { StartIdx = part.StartIdx; EndIdx = startI-1; SpanId = part.SpanId; Text = text1 }
                                    if text2 <> "" then
                                        yield { StartIdx = startI; EndIdx = endI-1; SpanId = Some sourceInfo.Id; Text = text2 }
                                    if text3 <> "" then
                                        yield { StartIdx = endI; EndIdx = part.EndIdx; SpanId = part.SpanId; Text = text3 }
                                ]
                            else if part.StartIdx > startI && part.EndIdx > endI-1 then
                                let text1, text2 = part.Text.[0..idx endI-1], part.Text.[idx endI..]
                                [
                                        yield {StartIdx = part.StartIdx; EndIdx = endI-1; SpanId = Some sourceInfo.Id; Text = text1}
                                        yield { StartIdx = endI; EndIdx = part.EndIdx; SpanId = part.SpanId; Text = text2 }
                                ]
                            else if part.StartIdx < startI && part.EndIdx < endI-1 then
                                let text1, text2 = part.Text.[0..idx startI], part.Text.[idx startI+1..]
                                [
                                        yield {StartIdx = part.StartIdx; EndIdx = startI-1; SpanId = part.SpanId; Text = text1}
                                        yield { StartIdx = startI; EndIdx = part.EndIdx; SpanId = Some sourceInfo.Id; Text = text2 }
                                ]                            
                            else [part]
                        )
                else
                    // selection doesn't involve the current paragraph
                    paragraphParts' 
                )
        )
    updatedParts
    |> List.map (fun part ->
        match part.SpanId with
        | Some id -> span [ ClassName ("span" + string id) ] [ str part.Text ]
        | None -> str part.Text)    


let view (model:Model) (dispatch: Msg -> unit) =
    [
        yield 
          div [ ClassName "container" ] [
            yield h1 [] [ str (model.Heading) ]
            yield div [ ClassName "article" ] [
                div [ ClassName "article-highlights" ] [
                    for idx, paragraph in (Array.zip [|0..model.Text.Length-1|] model.Text) do
                        yield p [ ]  (viewParagraphHighlights model idx paragraph) 
                ]
                div [ ClassName "article-text" ] [
                    for idx, paragraph in (Array.zip [|0..model.Text.Length-1|] model.Text) do
                        yield p [ OnMouseUp (fun _ -> dispatch (TextSelected (getSelection model))) 
                                  Id (string idx) ]  [ str paragraph ] 
                ]
            ]
            yield hr []
        ]

        yield div [ ClassName "container questionnaire" ] [
          match model.Q0_MentionsSources with
          | None ->
            yield h4 [] [ str "Does the article mention any sources?" ]
            yield button [ OnClick (fun _ -> dispatch (Q0_MentionsSources true)) 
                           (match model.Q0_MentionsSources with
                            | Some x -> if x then ClassName "btn btn-primary" else ClassName "btn btn-disabled"
                            | None -> ClassName "btn btn-light") ]
                         [ str "Yes" ]
            yield button [ (match model.Q0_MentionsSources with
                            | Some x -> if x then ClassName "btn btn-disabled" else ClassName "btn btn-primary"
                            | None -> ClassName "btn btn-light")
                           OnClick (fun _ -> dispatch (Q0_MentionsSources false)) ]
                         [ str "No" ]
          | Some false ->  ()
          | Some true ->
             for i in 0..model.SourceInfo.Length-1 do
                  yield viewAddSource model i dispatch
             yield button [
                    ClassName "btn btn-info"
                    OnClick (fun _ -> dispatch (AddSource (model.SourceInfo.Length))) ] 
                    [ str "+ Add additional source"]
        ]

    ]


 
let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | FetchArticle -> 
        model, fetchArticleCmd { Article.Title = model.Heading; ID = model.Link; Text = None }, NoOp
        
    | View -> 
        Browser.console.log("View message in update")
        model, Cmd.none, NoOp //DisplayArticle model.Article

    | TextSelected x ->
        match x with
        | Some (id, selection) ->
            if model.SourceInfo.Length < id+1 
            then 
                Browser.console.log("Source not added!")
                model, Cmd.none, NoOp
            else
                let modelInfo = model.SourceInfo


                let newInfoItem = { modelInfo.[id] with TextMentions = selection::modelInfo.[id].TextMentions }
                modelInfo.[id] <- newInfoItem

                { model with SourceInfo = modelInfo }, Cmd.none, NoOp
        | None -> 
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

    | Q0_MentionsSources x ->
        { model with Q0_MentionsSources = Some x; SourceInfo = [| { Id = 0; TextMentions = [] } |] },
        Cmd.none, NoOp

    | HighlightSource n ->
        { model with SourceSelectionMode = Some n }, 
        Cmd.none, NoOp

    | FinishedHighlighting ->
        { model with SourceSelectionMode = None}, Cmd.none, NoOp

    | ClearHighlights n ->
        let currentSources = model.SourceInfo
        if n < currentSources.Length then 
            currentSources.[n] <- { currentSources.[n] with TextMentions = [] }
            { model with SourceInfo = currentSources }, Cmd.none, NoOp
        else 
            Browser.console.log("Clear highlights from source " + string n + " but source not found.")
            model, Cmd.none, NoOp        

    | AddSource n ->
        let currentSources = model.SourceInfo
        { model with SourceInfo = Array.append currentSources [| { Id = n; TextMentions = [] } |] }, 
        Cmd.none, NoOp
