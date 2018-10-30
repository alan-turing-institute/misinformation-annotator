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
open Fable.Import

type HighlightMode =
    | SourceText of SourceId
    | AnonymityText of SourceId
    | NoHighlight

type HighlightType = | SourceHighlight | AnonymityReasonHighlight

type Model = {
    StartedEditing: System.DateTime
    User: UserData
    Heading: string
    Text: string []
    Link: string
    SourceWebsite: string
    MentionsSources: ArticleSourceType option
    SourceInfo : SourceInfo []
    SourceSelectionMode : HighlightMode
    ShowDeleteSelection : (SourceId * HighlightType * Selection) option
    Completed : bool
    Submitted : bool option
    FlaggedParsingErrors : bool
}

type Msg = 
    | View
    | EditAnnotations
    | FetchedArticle of Article * ArticleAnnotations option
    | FetchError of exn
    | SubmitError of exn
    | FetchArticle
    | TextSelected of (SourceId *Selection) 
    | MentionsSources of ArticleSourceType
    | HighlightSource of SourceId
    | FinishedHighlighting
    | ClearHighlights of int
    | AddSource of int
    | SubmitAnnotations
    | Submitted of AnswersResponse
    | DeletedAnnotations of AnswersResponse
    | ShowDeleteButton of (SourceId * HighlightType * Selection) 
    | RemoveDeleteButton
    | DeleteSelection of (SourceId * HighlightType * Selection)
    | IsSourceAnonymous of (SourceId * bool)
    | AnonymityReason of (SourceId * AnonymousInfo)
    | HighlightReason of SourceId
    | GoToNextArticle
    | SetNote of (int * string)
    | IncorrectlyParsed 
    | TagSuccess of AnswersResponse
    | TagError of exn
    | RemoveSource of SourceId

type ExternalMsg = 
    | NoOp    
    | NextArticle of string // pass the link to the current article
    | MarkAsAnnotated of string // mark current article as annotated


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
        return! Fetch.fetchAs<Article * ArticleAnnotations option> url props          
    }

let fetchArticleCmd article user = 
    Cmd.ofPromise fetchArticle (article, user) FetchedArticle FetchError


let postAnswers (model: Model) = 
    promise {
        let url = ServerUrls.APIUrls.Answers
        let ann = { 
                User = model.User
                Title = model.Heading
                ArticleID = model.Link
                ArticleType = model.MentionsSources.Value
                Annotations = model.SourceInfo
                MinutesSpent = (DateTime.Now - model.StartedEditing).TotalMinutes
                CreatedUTC = Some DateTime.UtcNow
                }
        let! response = 
            Fetch.postRecord url { Annotations = ann; Action = Save } []
        let! resp = response.json<AnswersResponse>()
        return resp }

let postAnswersCmd model = 
    Cmd.ofPromise postAnswers model Submitted SubmitError

let deleteAnnotations (model: Model) =
    promise {
        let url = ServerUrls.APIUrls.Answers
        let ann = { 
                User = model.User
                Title = model.Heading
                ArticleID = model.Link
                ArticleType = model.MentionsSources.Value
                Annotations = model.SourceInfo
                MinutesSpent = (DateTime.Now - model.StartedEditing).TotalMinutes
                CreatedUTC = Some DateTime.UtcNow }
        let! response = 
            Fetch.postRecord url { Annotations = ann; Action = Delete } []
        let! resp = response.json<AnswersResponse>()
        return resp }

let deleteAnnotationsCmd model =
    Cmd.ofPromise deleteAnnotations model DeletedAnnotations SubmitError

let flagIncorrectlyParsed (model: Model) =
    promise {
        let url = ServerUrls.APIUrls.ArticleError
        let! response = 
            Fetch.postRecord url { ArticleID = model.Link; User = model.User } []
        let! resp = response.json<AnswersResponse>()
        return resp }

let flagArticleCmd model =
    Cmd.ofPromise flagIncorrectlyParsed model TagSuccess TagError


let init (user:UserData) (article: Article)  = 
    { 
      User = user
      Heading = article.Title
      Text = match article.Text with | Some t -> t | None -> [||]
      Link = article.ID 
      SourceWebsite = article.SourceWebsite
      MentionsSources = None 
      SourceInfo = [||]
      SourceSelectionMode = NoHighlight
      ShowDeleteSelection = None 
      Submitted = None
      StartedEditing = DateTime.Now
      Completed = false
      FlaggedParsingErrors = false
      }, 
    fetchArticleCmd article user

let isInsideHelper paragraph position (selection : Selection) =    
    if selection.StartParagraphIdx = paragraph && selection.EndParagraphIdx = paragraph then
        position >= selection.StartIdx && position <= selection.EndIdx
    else if selection.StartParagraphIdx = paragraph then 
        position >= selection.StartIdx 
    else if selection.EndParagraphIdx = paragraph then
        position <= selection.EndIdx
    else if selection.StartParagraphIdx < paragraph && selection.EndParagraphIdx > paragraph then
        true
    else false

let isInsideSelection paragraph position (sources: SourceInfo []) =
    sources
    |> Array.choose (fun source -> 
        let sources =
            source.TextMentions 
            |> List.filter (fun (selection:Selection) -> 
                isInsideHelper paragraph position selection)
            |> List.map (fun s -> SourceHighlight, s)
        let anonymityReasons =
            match source.AnonymityReason with
            | Some(ar) ->
                ar |> List.filter (fun selection -> isInsideHelper paragraph position selection)
                |> List.map (fun s -> AnonymityReasonHighlight, s)
            | None -> []
        let activeSelections = List.append sources anonymityReasons
        if activeSelections.Length > 0 then
            Some (source.SourceID, activeSelections |> List.head)
        else None)

[<Emit("window.getSelection()")>]
let jsGetSelection () : obj = jsNative    

[<Emit("$0.toString()")>]
let jsExtractText (selection: obj) : string = jsNative

[<Emit("window.getSelection().removeAllRanges()")>]
let jsRemoveSelection() = jsNative

type SelectionResult = 
    | NoSelection
    | NewHighlight of SourceId * Selection
    | ClickHighlight of SourceId * HighlightType * Selection

let getSelection (model: Model) e : SelectionResult = 
    Browser.console.log(jsGetSelection())
    let rawOutput = jsGetSelection()

    match string (rawOutput?("type")) with
    | "Range" -> 
        match model.SourceSelectionMode with
        | NoHighlight -> NoSelection
        | SourceText(id) | AnonymityText(id) ->
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
            let sourceId, (selectionType, selected) = clickedSelection |> Array.head
            ClickHighlight (sourceId, selectionType, selected)

    | _ -> NoSelection
    
let viewAddSource (model: Model) n (dispatch: Msg -> unit) =
    div [ClassName "container col-sm-12"] [
        div [ClassName "row"] [
            h4 [ ClassName ("question" + string n) ] [ str ("Source number " + string (n+1)) ]
            input [ HTMLAttr.Type "text"
                    ClassName "form-control input-md"
                    Placeholder "Notes"
                    DefaultValue ""
                    AutoFocus false
                    OnChange (fun ev -> dispatch (SetNote (n,!!ev.target?value)))
                    ]
            button [ 
                ClassName "btn btn-white .small pull-right"
                OnClick (fun _ -> dispatch (RemoveSource n)) ] 
                [str "╳  Delete source"]  
        ]      
        div [ClassName "row"] [
        ol [ ] [
            yield li [ ] 
               [ str "Highlight the portion of the text that refers to this source."
                 br []
                 str "There may be multiple sections in the text that refer to the same source - please highlight all of them. Click Start to start highlighting, and Finish to complete highlighting."
                 br []
                 button [ OnClick (fun _ -> dispatch (HighlightSource n))
                          (match model.SourceSelectionMode with
                           | SourceText _ -> ClassName "btn btn-disabled" 
                           | NoHighlight -> ClassName "btn btn-info"
                           | AnonymityText _ -> ClassName "btn btn-info") ] 
                          [ (if model.SourceInfo.[n].TextMentions.Length = 0 
                             then str "Start" 
                             else str "Continue") ]
                 button [ OnClick (fun _ -> dispatch (FinishedHighlighting))
                          (match model.SourceSelectionMode with
                           | SourceText i -> if i = n then ClassName "btn btn-info" else ClassName "btn btn-disabled"
                           | NoHighlight -> ClassName "btn btn-disabled"
                           | AnonymityText _ -> ClassName "btn btn-disabled") ] 
                          [ str "Finish" ]
                 button [ OnClick (fun _ -> dispatch (ClearHighlights n)) 
                          ClassName "btn btn-secondary" ] 
                          [ str "Clear highlights" ] ]
            
            yield li [] [ 
                    str "Is the source named or anonymous?" 
                    br []
                    button [ 
                        (match model.SourceInfo.[n].SourceType with
                         | None -> ClassName "btn btn-light"
                         | Some Named -> ClassName "btn btn-info"
                         | Some (Anonymous _) -> ClassName "btn btn-light")
                        OnClick (fun _ -> dispatch (IsSourceAnonymous (n, false))) ] 
                        [ str "Not anonymous" ]
                    button [ 
                        (match model.SourceInfo.[n].SourceType with
                         | None -> ClassName "btn btn-light"
                         | Some Named -> ClassName "btn btn-light"
                         | Some (Anonymous _) -> ClassName "btn btn-info")
                        OnClick (fun _ -> dispatch (IsSourceAnonymous (n, true))) ] 
                        [ str "Anonymous" ]
                ]
            match model.SourceInfo.[n].SourceType with
            | None -> yield! []
            | Some(Named) -> yield! []
            | Some(Anonymous) ->
              yield! [
                  li [] [ 
                      str "Is there a reason given for providing anonymity?" 
                      br []
                      button [
                        (match model.SourceInfo.[n].AnonymousInfo with 
                         | None ->
                            ClassName "btn btn-secondary"
                         | Some NoReasonGiven ->
                            ClassName "btn btn-disabled"
                         | Some Reason ->
                            ClassName "btn btn-info")
                        OnClick (fun _ -> dispatch (AnonymityReason(n, Reason)))
                        ] [ str "Yes" ]
                      button [
                        (match model.SourceInfo.[n].AnonymousInfo with 
                         | None ->
                            ClassName "btn btn-secondary"
                         | Some NoReasonGiven ->
                            ClassName "btn btn-info"
                         | Some Reason ->
                            ClassName "btn btn-disabled")
                        OnClick (fun _ -> dispatch (AnonymityReason(n, NoReasonGiven)))
                        ] [ str "No" ]
                      ]
                ]
              if model.SourceInfo.[n].AnonymousInfo = Some(Reason) then
                yield! [
                  yield 
                    li [] [ str "Please highlight the portion of the text where the reason for anonymity is given."]
                  yield 
                    button [
                        (match model.SourceSelectionMode with
                         | AnonymityText id ->
                            if id = n then ClassName "btn btn-disabled"
                            else ClassName "btn btn-info"
                         | NoHighlight | SourceText _ -> 
                            ClassName "btn btn-info")
                        OnClick (fun _ -> dispatch (HighlightReason n))
                    ] [ str "Start" ] 
                  yield
                    button [
                        (match model.SourceSelectionMode with
                         | AnonymityText id ->
                            if id = n then ClassName "btn btn-info"
                            else ClassName "btn btn-disabled"
                         | NoHighlight | SourceText _ ->
                            ClassName "btn btn-disabled")
                        OnClick (fun _ -> dispatch (FinishedHighlighting))
                    ] [ str "Finish" ]
                ]
        ]
        ]
    ]

type ParagraphPart = {
    StartIdx : int
    EndIdx : int 
    SpanId : HighlightMode option
    Text : string
}


let getSpanID id selectionType =
  match selectionType with
  | SourceHighlight -> Some (SourceText id)
  | AnonymityReasonHighlight -> Some (AnonymityText id)
  

let viewParagraphHighlights (model: Model) paragraphIdx (text: string) (dispatch: Msg -> unit) =
    if model.SourceInfo.Length = 0 then [ str text ] else

    // split up into pieces first, add span attributes later
    let initialState = [{StartIdx = 0; EndIdx=text.Length-1; SpanId = None; Text = text }]
    let updatedParts = 
        (initialState, model.SourceInfo)
        ||> Array.fold (fun paragraphParts sourceInfo ->
            // selected specific source, loop over highlights
            let allSelections = 
              List.append
                (sourceInfo.TextMentions |> List.map (fun tm -> SourceHighlight, tm))
                (match sourceInfo.AnonymityReason with 
                 | None -> [] 
                 | Some(rs) -> rs |> List.map (fun r -> AnonymityReasonHighlight, r))
                
            (paragraphParts, allSelections) 
            ||> List.fold (fun paragraphParts' (selectionType, selection) -> 
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
                                [ { part with SpanId = getSpanID sourceInfo.SourceID selectionType } ] 
                            else if part.StartIdx <= startI && part.EndIdx >= endI-1 then
                                let text1,text2,text3 = 
                                    part.Text.[0.. idx startI-1], 
                                    part.Text.[idx startI .. idx endI-1], 
                                    part.Text.[idx endI ..]
                                [   
                                    if text1 <> "" then 
                                        yield { StartIdx = part.StartIdx; EndIdx = startI-1; SpanId = part.SpanId; Text = text1 }
                                    if text2 <> "" then
                                        yield { StartIdx = startI; EndIdx = endI-1; SpanId = getSpanID sourceInfo.SourceID selectionType; Text = text2 }
                                    if text3 <> "" then
                                        yield { StartIdx = endI; EndIdx = part.EndIdx; SpanId = part.SpanId; Text = text3 }
                                ]
                            else if part.StartIdx > startI && part.EndIdx > endI-1 then
                                let text1, text2 = part.Text.[0..idx endI-1], part.Text.[idx endI..]
                                [
                                        yield {StartIdx = part.StartIdx; EndIdx = endI-1; SpanId = getSpanID sourceInfo.SourceID selectionType; Text = text1}
                                        yield { StartIdx = endI; EndIdx = part.EndIdx; SpanId = part.SpanId; Text = text2 }
                                ]
                            else if part.StartIdx < startI && part.EndIdx < endI-1 then
                                let text1, text2 = part.Text.[0..idx startI], part.Text.[idx startI+1..]
                                [
                                        yield {StartIdx = part.StartIdx; EndIdx = startI-1; SpanId = part.SpanId; Text = text1}
                                        yield { StartIdx = startI; EndIdx = part.EndIdx; SpanId = getSpanID sourceInfo.SourceID selectionType; Text = text2 }
                                ]                            
                            else [part]
                        )
                else
                    // selection doesn't involve the current paragraph
                    paragraphParts' 
                )
        )

    updatedParts
    |> List.filter (fun part -> part.Text.Length > 0)
    |> List.map (fun part ->
        match part.SpanId with
        | Some (SourceText id) ->
            span [ ClassName ("span" + string id)
                      ] [ str part.Text ]
        | Some (AnonymityText id) ->
            span [ ClassName ("anon" + string id) ] [ str part.Text ]
        | Some (NoHighlight) ->
            str part.Text
        | None -> str part.Text)    


let view (model:Model) (dispatch: Msg -> unit) =
  [
    div [ ClassName "col-md-10"] [
      div [ ClassName "row row-height" ] [
       yield 
        div [ 
          ClassName "container col-md-6 left"
          OnMouseDown (fun e -> 
            let target = e.target |> unbox<Browser.HTMLButtonElement>
            if not (target.classList.contains "delete-highlight-btn") then
              match model.ShowDeleteSelection with
              | Some _ -> e.preventDefault()
              | None -> ()
              dispatch RemoveDeleteButton
            else ()) ] [
          yield h1 [] [ str (model.Heading) ]
          yield h4 [] [ str (model.SourceWebsite)]
          yield 
            div [ ClassName "article" ] [
              div [ ClassName "article-highlights container col-sm-12" ] 
                [ for idx, paragraph in (Array.zip [|0..model.Text.Length-1|] model.Text) do
                    yield p [ ]  (viewParagraphHighlights model idx paragraph dispatch) ]
              div [ ClassName "article-text container col-sm-12" ] 
                [ for idx, paragraph in (Array.zip [|0..model.Text.Length-1|] model.Text) do
                    yield p  
                      [ OnMouseUp (fun e -> 
                          match (getSelection model e) with
                          | NewHighlight(id, selection) -> 
                             dispatch (TextSelected (id,selection))
                          | ClickHighlight(id, selectionType, selection) -> 
                             dispatch (ShowDeleteButton (id, selectionType, selection))
                          | NoSelection -> () )
                        Id (string idx) ]  
                      [ match model.ShowDeleteSelection with 
                        | None -> 
                          yield str paragraph
                        | Some (id, selectionType, selection) ->
                          if selection.EndParagraphIdx <> idx then
                            yield str paragraph 
                          else
                            let part1 = paragraph.[..selection.EndIdx-1]
                            let part2 = paragraph.[selection.EndIdx..]
                            yield span [] [ str part1 ]
                            yield button [ 
                                   ClassName "btn btn-danger delete-highlight-btn" 
                                   OnClick (fun _ -> dispatch (DeleteSelection (id, selectionType, selection)))]
                                   [ str "Delete" ]
                            yield span [] [ str part2 ]
                       ] 
                ]
            ]
          yield hr []
          yield br [] 
          yield br []
          yield 
            button [ (if not model.FlaggedParsingErrors then 
                        OnClick (fun _ -> dispatch IncorrectlyParsed) 
                      else OnClick (fun _ -> ()))
                     ClassName "btn btn-link .small" ] 
                   [ (if not model.FlaggedParsingErrors then
                        str "Garbled text? Flag article as incorrectly parsed."
                      else 
                        str "Article flagged as incorrectly parsed.") ]
         ]

       yield div [ ClassName "container col-md-6 right" ] [
         div [ ClassName "questionnaire w-100"] [
          match model.Submitted with
          | Some true ->
            yield h5 [] [ str "Submitted" ]
            yield button [ OnClick (fun _ -> dispatch GoToNextArticle)
                           ClassName "btn btn-success" ]
                         [ str "Go to next article" ]
            yield button [ OnClick (fun _ -> dispatch EditAnnotations )
                           ClassName "btn btn-light" ] 
                          [ str "Edit annotations" ]
          | Some false | None ->              
            yield h4 [] [ str "Does the article mention any sources?" ]
            yield button [ 
                OnClick (fun _ -> dispatch (MentionsSources Sourced)) 
                (match model.MentionsSources with
                    | Some Sourced -> ClassName "btn btn-primary" 
                    | Some _ -> ClassName "btn btn-disabled"
                    | None -> ClassName "btn btn-light") ]
                [ str "Yes" ]
            yield button [ 
                (match model.MentionsSources with
                  | Some Unsourced -> ClassName "btn btn-primary" 
                  | Some _ -> ClassName "btn btn-disabled"
                  | None -> ClassName "btn btn-light")
                OnClick (fun _ -> dispatch (MentionsSources Unsourced)) ]
                [ str "No" ]
            yield button [ 
                (match model.MentionsSources with
                  | Some NotRelevant -> ClassName "btn btn-primary"
                  | Some _ -> ClassName "btn btn-disabled"
                  | None -> ClassName "btn btn-light")
                OnClick (fun _ -> dispatch (MentionsSources NotRelevant)) ]
                [ str "Mark as not relevant" ]

            match model.MentionsSources with
              | None | Some Unsourced | Some NotRelevant ->
                ()
              | Some Sourced ->
                 for i in 0..model.SourceInfo.Length-1 do
                      yield viewAddSource model i dispatch
                      
                 yield button 
                        (if model.SourceInfo.Length < 10 then 
                            [ ClassName "btn btn-primary"
                              OnClick (fun _ -> dispatch (AddSource (model.SourceInfo.Length))) ]
                         else
                            [ ClassName "btn btn-disabled" ]) 
                        [ str "+ Add additional source"]
            yield 
                div [] [
                    yield hr []
                    yield button 
                        [ OnClick (fun _ -> dispatch (SubmitAnnotations))
                          (if model.Completed then ClassName "btn btn-success" 
                           else ClassName "btn btn-disabled") ] 
                        [ str "Submit" ]
                    if model.Submitted = Some false then
                        yield span [] [str "Cannot submit - please contact the admin."]
                ]
        ]
       ]
       ]
      ]
    ]

// Check if form can be submitted
let checkQuestionsCompleted (model: Model) =
    // is source-specific part completed?
    let isPartCompleted (sourceInfo: SourceInfo) =
        if sourceInfo.TextMentions.Length = 0 then false
        else 
        match sourceInfo.SourceType with
        | None -> false
        | Some Named -> true
        | Some Anonymous ->
            match sourceInfo.AnonymousInfo with
            | None -> false
            | Some NoReasonGiven -> true
            | Some Reason ->
                match sourceInfo.AnonymityReason with
                | None -> false
                | Some text ->
                    if text.Length = 0 then false
                    else true

    if model.MentionsSources = Some Unsourced then true else
    if model.MentionsSources = Some NotRelevant then true else
    if model.SourceInfo.Length = 0 then false else
    (true, model.SourceInfo)
    ||> Array.fold (fun state si -> state && isPartCompleted si)

let isCompleted model = 
    { model with Completed = checkQuestionsCompleted model }    
 
let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | FetchArticle -> 
        model |> isCompleted, 
        fetchArticleCmd 
            { Article.Title = model.Heading; 
              ID = model.Link; 
              Text = None; 
              SourceWebsite = model.SourceWebsite
              AssignmentType = Standard } model.User,
            NoOp
         
    | View -> 
        Browser.console.log("View message in update")
        model |> isCompleted, Cmd.none, NoOp //DisplayArticle model.Article

    | EditAnnotations ->
        { model with 
            Completed = true
            Submitted = None },
        Cmd.none, NoOp

    | DeletedAnnotations _ ->
        model, Cmd.none, NoOp

    | TextSelected (id, selection) ->
        if model.SourceInfo.Length < id+1 
        then 
            Browser.console.log("Source not added!")
            model |> isCompleted, Cmd.none, NoOp
        else

            match model.SourceSelectionMode with
            | NoHighlight -> model, Cmd.none, NoOp
            | SourceText x ->
                if x <> id then Browser.console.log("Inconsistent source numbers")
                let modelInfo = model.SourceInfo
                let newInfoItem = { 
                    modelInfo.[id] with 
                      TextMentions = selection::modelInfo.[id].TextMentions 
                      }
                modelInfo.[id] <- newInfoItem
                { model with SourceInfo = modelInfo } |> isCompleted, Cmd.none, NoOp
            | AnonymityText x ->
                if x <> id then Browser.console.log("Anonymity reason: inconsistent source numbers")

                let modelInfo = model.SourceInfo
                let newInfoItem = {
                    modelInfo.[id] with
                        AnonymityReason = 
                            match modelInfo.[id].AnonymityReason with
                            | None -> Some [selection]
                            | Some(s) -> Some (selection::s)
                }
                modelInfo.[id] <- newInfoItem
                { model with SourceInfo = modelInfo } |> isCompleted, Cmd.none, NoOp

    | FetchedArticle (article, annotations) ->
        Browser.console.log("Fetched article!")
        
        match article.Text with
        | Some t ->         
            Browser.console.log(t.[0])
            
            match annotations with
            | Some a -> 
                if a.ArticleID = article.ID && a.User.UserName = model.User.UserName then
                    { model with 
                        Text = t; 
                        MentionsSources = Some a.ArticleType
                        SourceInfo = a.Annotations; 
                        Submitted = Some true }
                    |> isCompleted, Cmd.none, NoOp
                else
                    Browser.console.log("Annotations loaded for incorrect user.")
                    { model with Text = t }|> isCompleted, Cmd.none, NoOp
            | None -> 
                { model with Text = t }|> isCompleted, Cmd.none, NoOp
        | None -> model|> isCompleted, Cmd.none, NoOp

    | FetchError e ->
        model|> isCompleted, Cmd.none, NoOp

    | MentionsSources x ->
        match x with 
        | Sourced ->
            { model with 
                MentionsSources = Some x; 
                SourceInfo = 
                  [|  { SourceID = 0; 
                        TextMentions = []; 
                        SourceType = None; 
                        AnonymousInfo = None; 
                        AnonymityReason = None
                        UserNotes = None } |] }
                |> isCompleted,
            Cmd.none, NoOp
        | Unsourced | NotRelevant ->
            { model with 
                MentionsSources = Some x; 
                SourceInfo = [||] }
                |> isCompleted,
            Cmd.none, NoOp        

    | HighlightSource n -> 
        { model with SourceSelectionMode = SourceText n } |> isCompleted, 
        Cmd.none, NoOp

    | FinishedHighlighting ->
        { model with SourceSelectionMode = NoHighlight} |> isCompleted, Cmd.none, NoOp

    | ClearHighlights n ->
        let currentSources = model.SourceInfo
        if n < currentSources.Length then 
            currentSources.[n] <- { currentSources.[n] with TextMentions = [] }
            { model with SourceInfo = currentSources } |> isCompleted, Cmd.none, NoOp
        else 
            Browser.console.log("Clear highlights from source " + string n + " but source not found.")
            model |> isCompleted, Cmd.none, NoOp        

    | AddSource n ->
        let currentSources = model.SourceInfo
        { model with 
            SourceInfo = 
                Array.append 
                    currentSources 
                    [| { SourceID = n; TextMentions = []; SourceType = None; AnonymousInfo = None; AnonymityReason = None; UserNotes = None } |] }
        |> isCompleted, 
        Cmd.none, NoOp

    | SubmitAnnotations ->
        model |> isCompleted, 
        (if model.Completed then postAnswersCmd model else Cmd.none), 
        NoOp

    | Submitted resp ->
        match resp.Success with 
        | true -> 
            { model with Submitted = Some true } |> isCompleted, Cmd.none, ExternalMsg.MarkAsAnnotated model.Link
        | false -> 
            { model with Submitted = Some false } |> isCompleted, Cmd.none, NoOp
    
    | SubmitError e ->
        { model with Submitted = Some false } |> isCompleted,
        Cmd.none, NoOp

    | ShowDeleteButton selection ->
        Browser.console.log("Mouse click on highlight!")
        { model with ShowDeleteSelection = Some selection } |> isCompleted,
        Cmd.none, NoOp

    | RemoveDeleteButton ->
        Browser.console.log("Removing delete button")
        { model with ShowDeleteSelection = None} |> isCompleted,
        Cmd.none, NoOp

    | DeleteSelection (id, selectionType, selection : Selection) ->
        Browser.console.log("Delete!!!")

        match selectionType with
        | SourceHighlight ->
            let newHighlights = 
                model.SourceInfo.[id].TextMentions
                |> List.filter (fun s -> s <> selection)
            let newSources = model.SourceInfo
            newSources.[id] <- { model.SourceInfo.[id] with TextMentions = newHighlights }

            { model with 
                ShowDeleteSelection = None;
                SourceInfo = newSources }
                |> isCompleted,
            Cmd.none, NoOp

        | AnonymityReasonHighlight ->
          match model.SourceInfo.[id].AnonymityReason with
          | None -> 
            Browser.console.log("Deleting non-existing reason for anonymity.")
            model, Cmd.none, NoOp
          | Some ar ->
            let newHighlights = 
                let ar' = ar |> List.filter (fun s -> s <> selection)
                if ar'.Length > 0 then Some ar' else None
            let newSources = model.SourceInfo
            newSources.[id] <- { model.SourceInfo.[id] with AnonymityReason = newHighlights }

            { model with 
                ShowDeleteSelection = None;
                SourceInfo = newSources }
                |> isCompleted,
            Cmd.none, NoOp
            
    | IsSourceAnonymous (id, isAnonymous) ->
        let sources = model.SourceInfo
        sources.[id] <- { 
            sources.[id] with 
              SourceType = 
                if not isAnonymous then Some(Named) 
                else Some(Anonymous) }
        { model with 
            SourceInfo = sources }
            |> isCompleted, Cmd.none, NoOp

    | AnonymityReason (id, reason) ->
        let sources = model.SourceInfo
        sources.[id] <- {
            sources.[id] with 
                AnonymousInfo =
                    if sources.[id].SourceType = Some(Anonymous) then
                        Some reason
                    else None
        }        
        { model with 
            SourceInfo = sources } |> isCompleted, Cmd.none, NoOp

    | HighlightReason id ->
        { model with SourceSelectionMode = AnonymityText id } |> isCompleted, Cmd.none, NoOp        

    | GoToNextArticle ->
        model, Cmd.none, NextArticle model.Link

    | SetNote (sourceIdx, text) ->
        let sourceInfo = model.SourceInfo
        sourceInfo.[sourceIdx] <- 
            { sourceInfo.[sourceIdx] with UserNotes = (if text <> "" then Some text else None) }
        { model with SourceInfo = sourceInfo }, Cmd.none, NoOp

    | IncorrectlyParsed ->
        model, flagArticleCmd model, NoOp

    | TagSuccess result ->
        if result.Success then 
            { model with FlaggedParsingErrors = true }, Cmd.none, NoOp
        else 
            model, Cmd.none, NoOp

    | TagError _ -> { model with FlaggedParsingErrors = false }, Cmd.none, NoOp

    | RemoveSource sourceId ->
        // remove entire source and relabel other sources?
        let sources = 
            model.SourceInfo
            |> Array.indexed 
            |> Array.choose (fun (i, source) ->
                if i <> sourceId then
                    if i > sourceId then 
                        Some { source with SourceID = i - 1 }
                    else Some source
                else 
                    None)

        { model with SourceInfo = sources }, Cmd.none, NoOp
