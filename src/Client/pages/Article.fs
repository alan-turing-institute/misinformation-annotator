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
    User: UserData
    Heading: string
    Text: string [] 
    Link: string
    Tags: string list
    Q0_MentionsSources: bool option
    SourceInfo : SourceInfo []
    SourceSelectionMode : SourceId option // id of the source that's being currently annotated
    ShowDeleteSelection : (SourceId * Selection) option
}

type Msg = 
    | View
    | FetchedArticle of Article
    | FetchError of exn
    | FetchArticle
    | TextSelected of (SourceId *Selection) 
    | Q0_MentionsSources of bool
    | HighlightSource of int
    | FinishedHighlighting
    | ClearHighlights of int
    | AddSource of int
    | SubmitAnnotations
    | Submitted of AnswersResponse
    | ShowDeleteButton of (SourceId * Selection) 
    | RemoveDeleteButton
    | DeleteSelection of (SourceId * Selection)

type ExternalMsg = 
    | DisplayArticle of Article
    | NoOp    


let fetchArticle (article : Domain.Article, user : Domain.UserData) =
    promise {
        let url = ServerUrls.APIUrls.Article
        let body = toJson article
        let props = 
            [ RequestProperties.Method HttpMethod.POST
              Fetch.requestHeaders [
                HttpRequestHeaders.Authorization ("Bearer " + user.Token)
                HttpRequestHeaders.ContentType "application/json" ]
              RequestProperties.Body !^body ]
        return! Fetch.fetchAs<Article> url props          
    }

let fetchArticleCmd article user = 
    Cmd.ofPromise fetchArticle (article, user) FetchedArticle FetchError


let postAnswers (model: Model) = 
    promise {
        let url = ServerUrls.APIUrls.Answers
        let! response = 
            Fetch.postRecord url { 
                User = model.User
                Title = model.Heading
                ArticleID = model.Link
                Annotations = model.SourceInfo } []
        let! resp = response.json<AnswersResponse>()
        return resp }

let postAnswersCmd model = 
    Cmd.ofPromise postAnswers model Submitted FetchError

let init (user:UserData) (article: Article)  = 
    { 
      User = user
      Heading = article.Title
      Text = match article.Text with | Some t -> t | None -> [||]
      Tags = []
      Link = article.ID 
      Q0_MentionsSources = None 
      SourceInfo = [||]
      SourceSelectionMode = None
      ShowDeleteSelection = None }, 
    fetchArticleCmd article user

let isInsideSelection paragraph position (sources: SourceInfo []) =
    sources
    |> Array.choose (fun source -> 
        let selected =
            source.TextMentions 
            |> List.filter (fun (selection:Selection) ->
                if selection.StartParagraphIdx = paragraph && selection.EndParagraphIdx = paragraph then
                    position >= selection.StartIdx && position <= selection.EndIdx
                else if selection.StartParagraphIdx = paragraph then 
                    position >= selection.StartIdx 
                else if selection.EndParagraphIdx = paragraph then
                    position <= selection.EndIdx
                else if selection.StartParagraphIdx < paragraph && selection.EndParagraphIdx > paragraph then
                    true
                else false)
        
        if selected.Length > 0 then Some(source.SourceID, selected |> List.head) else None)


[<Emit("window.getSelection()")>]
let jsGetSelection () : obj = jsNative    

[<Emit("$0.toString()")>]
let jsExtractText (selection: obj) : string = jsNative

[<Emit("window.getSelection().removeAllRanges()")>]
let jsRemoveSelection() = jsNative

type SelectionResult = 
    | NoSelection
    | NewHighlight of SourceId * Selection
    | ClickHighlight of SourceId * Selection

let getSelection (model: Model) e : SelectionResult = 
    Browser.console.log(jsGetSelection())
    let rawOutput = jsGetSelection()

    match string (rawOutput?("type")) with
    | "Range" -> 
        match model.SourceSelectionMode with
        | None -> NoSelection
        | Some id ->
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

            let result = 
             (id, { 
               StartParagraphIdx = startP
               StartIdx = startI
               EndParagraphIdx = endP
               EndIdx = endI
               Text = jsExtractText(rawOutput)})

            jsRemoveSelection()
            result |> NewHighlight
    | "Caret" -> 
        let paragraph = rawOutput?anchorNode?parentElement?id |> unbox<int>
        let position = rawOutput?anchorOffset |> unbox<int>

        let clickedSelection = 
            model.SourceInfo 
            |> isInsideSelection paragraph position 

        if clickedSelection.Length = 0 then
            NoSelection
        else
            let selected = clickedSelection |> Array.exactlyOne
            ClickHighlight selected

    | _ -> NoSelection
    
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

let viewParagraphHighlights (model: Model) paragraphIdx (text: string) (dispatch: Msg -> unit) =
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
                                [ { part with SpanId = Some sourceInfo.SourceID } ] 
                            else if part.StartIdx <= startI && part.EndIdx >= endI-1 then
                                let text1,text2,text3 = 
                                    part.Text.[0.. idx startI-1], 
                                    part.Text.[idx startI .. idx endI-1], 
                                    part.Text.[idx endI ..]
                                [   
                                    if text1 <> "" then 
                                        yield { StartIdx = part.StartIdx; EndIdx = startI-1; SpanId = part.SpanId; Text = text1 }
                                    if text2 <> "" then
                                        yield { StartIdx = startI; EndIdx = endI-1; SpanId = Some sourceInfo.SourceID; Text = text2 }
                                    if text3 <> "" then
                                        yield { StartIdx = endI; EndIdx = part.EndIdx; SpanId = part.SpanId; Text = text3 }
                                ]
                            else if part.StartIdx > startI && part.EndIdx > endI-1 then
                                let text1, text2 = part.Text.[0..idx endI-1], part.Text.[idx endI..]
                                [
                                        yield {StartIdx = part.StartIdx; EndIdx = endI-1; SpanId = Some sourceInfo.SourceID; Text = text1}
                                        yield { StartIdx = endI; EndIdx = part.EndIdx; SpanId = part.SpanId; Text = text2 }
                                ]
                            else if part.StartIdx < startI && part.EndIdx < endI-1 then
                                let text1, text2 = part.Text.[0..idx startI], part.Text.[idx startI+1..]
                                [
                                        yield {StartIdx = part.StartIdx; EndIdx = startI-1; SpanId = part.SpanId; Text = text1}
                                        yield { StartIdx = startI; EndIdx = part.EndIdx; SpanId = Some sourceInfo.SourceID; Text = text2 }
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
        | Some id -> 
            match model.ShowDeleteSelection with
            | Some (id', selection) -> 
                if id' = id && selection.Text = part.Text then
                    span [ ClassName ("span" + string id) ] [
                        str part.Text
                    ]
                else
                    span [ ClassName ("span" + string id)
                            ] [ str part.Text ]
            | None ->
                span [ ClassName ("span" + string id)
                      ] [ str part.Text ]
        | None -> str part.Text)    


let view (model:Model) (dispatch: Msg -> unit) =
    [
      div [ ClassName "col-md-10"] [
        div [ ClassName "row" ] [
         yield 
          div [ ClassName "container col-lg-6"
                OnMouseDown (fun e -> 
                    if not (e.target.ToString().Contains "delete-highlight-btn") then
                        match model.ShowDeleteSelection with
                        | Some _ -> 
                            e.preventDefault()
                        | None -> ()
                        dispatch RemoveDeleteButton
                    else ()) ] [
            yield h1 [] [ str (model.Heading) ]
            yield div [ ClassName "article" ] [
                div [ ClassName "article-highlights" ] [
                    for idx, paragraph in (Array.zip [|0..model.Text.Length-1|] model.Text) do
                        yield p [ ]  (viewParagraphHighlights model idx paragraph dispatch) 
                ]
                div [ ClassName "article-text" ] [
                    for idx, paragraph in (Array.zip [|0..model.Text.Length-1|] model.Text) do
                        yield p [ OnMouseUp (fun e -> 
                                    match (getSelection model e) with
                                    | NewHighlight(id, selection) -> dispatch (TextSelected (id,selection))
                                    | ClickHighlight(position, selection) -> dispatch (ShowDeleteButton (position, selection))
                                    | NoSelection -> () 
                                    )
                                  Id (string idx) ]  [ 
                                  match model.ShowDeleteSelection with 
                                  | None -> 
                                        yield str paragraph
                                  | Some (id, selection) ->
                                    if selection.EndParagraphIdx <> idx then
                                        yield str paragraph 
                                    else
                                        let part1 = paragraph.[..selection.EndIdx-1]
                                        let part2 = paragraph.[selection.EndIdx..]
                                        yield span [] [ str part1 ]
                                        yield button [ 
                                               ClassName "btn btn-danger delete-highlight-btn" 
                                               OnClick (fun _ -> dispatch (DeleteSelection (id, selection)))]
                                               [ str "Delete" ]
                                        yield span [] [ str part2 ]
                                   ] 
                ]
            ]
            yield hr []
         ]

         yield div [ ClassName "container questionnaire sticky-top col-lg-6" ] [
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
          yield 
            div [] [
                hr []
                button 
                    [ OnClick (fun _ -> dispatch (SubmitAnnotations))
                      ClassName "btn btn-primary" ] 
                    [ str "Submit" ]
            ]
        ]
        ]
      ]
    ]


 
let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | FetchArticle -> 
        model, 
        fetchArticleCmd 
            { Article.Title = model.Heading; ID = model.Link; Text = None } model.User,
            NoOp
        
    | View -> 
        Browser.console.log("View message in update")
        model, Cmd.none, NoOp //DisplayArticle model.Article

    | TextSelected (id, selection) ->
        match model.ShowDeleteSelection with
        | None ->
            if model.SourceInfo.Length < id+1 
            then 
                Browser.console.log("Source not added!")
                model, Cmd.none, NoOp
            else
                let modelInfo = model.SourceInfo
                Browser.console.log("Text: ")
                Browser.console.log(modelInfo.[id].TextMentions)
                let newInfoItem = { modelInfo.[id] with TextMentions = selection::modelInfo.[id].TextMentions }
                modelInfo.[id] <- newInfoItem

                { model with SourceInfo = modelInfo }, Cmd.none, NoOp
        | Some _ -> 
                { model with ShowDeleteSelection = None }, Cmd.none, NoOp            

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
        { model with Q0_MentionsSources = Some x; SourceInfo = [| { SourceID = 0; TextMentions = [] } |] },
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
        { model with SourceInfo = Array.append currentSources [| { SourceID = n; TextMentions = [] } |] }, 
        Cmd.none, NoOp

    | SubmitAnnotations ->
        model, postAnswersCmd model, NoOp

    | Submitted resp ->
        match resp.Success with 
        | true -> Browser.console.log("Successfully submitted")
        | false -> Browser.console.log("Unsuccessful")
        model, Cmd.none, NoOp

    | ShowDeleteButton selection ->
        Browser.console.log("Mouse click on highlight!")
        { model with ShowDeleteSelection = Some selection },
        Cmd.none, NoOp

    | RemoveDeleteButton ->
        Browser.console.log("Removing delete button")
        { model with ShowDeleteSelection = None},
        Cmd.none, NoOp

    | DeleteSelection (id, selection : Selection) ->
        Browser.console.log("Delete!!!")

        let newHighlights = 
            model.SourceInfo.[id].TextMentions
            |> List.filter (fun s -> s <> selection)
        let newSources = model.SourceInfo
        newSources.[id] <- { model.SourceInfo.[id] with TextMentions = newHighlights }

        { model with 
            ShowDeleteSelection = None;
            SourceInfo = newSources },
        Cmd.none, NoOp