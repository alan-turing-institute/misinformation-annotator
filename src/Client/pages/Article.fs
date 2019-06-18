module Client.Article

open Fable.Core
open Fable.Import
open Fable.Import.Browser
open Fable.Import.React
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
open Client.HtmlElements


type HighlightMode =
    | SourceText of SourceId
    | AnonymityText of SourceId
    | NoHighlight

type HighlightType = | SourceHighlight | AnonymityReasonHighlight

type ArticlePageElementType = BaseText | HighlightedText

type Model = {
    StartedEditing: System.DateTime
    User: UserData
    Heading: string
    Text: ArticleText
    Link: string
    SourceWebsite: string
    MentionsSources: ArticleSourceType option
    SourceInfo : SourceInfo []
    SourceSelectionMode : HighlightMode
    ShowDeleteSelection : (SourceId * HighlightType * Selection) option
    Completed : bool
    Submitted : bool option
    FlaggedParsingErrors : bool
    Loading : bool
}

type Msg = 
    | EditAnnotations
    | FetchedArticle of Article * ArticleAnnotations option
    | FetchError of exn
    | SubmitError of exn
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
    | GetNextArticle 
    | MarkAsAnnotated of string // mark current article as annotated

//==================================================
// Code to compare article IDs
let compare (x: IdAttribute) (y: IdAttribute) = 
    if x = y then 0  
    else
    let xs = x.Split '.' |> Array.map int
    let ys = y.Split '.' |> Array.map int
    let xs', ys' =
        if xs.Length = ys.Length then xs, ys 
        else
            if xs.Length > ys.Length then 
                xs, Array.append ys (Array.init (xs.Length - ys.Length) (fun _ -> 0))
            else
                Array.append xs (Array.init (ys.Length - xs.Length) (fun _ -> 0)), ys
    let result, decided =
        (xs', ys')
        ||> Array.zip
        |> Array.fold (fun (res, decid) (a,b) -> 
            if not decid then
                if a = b then (0, false)
                else 
                    if a < b then (-1, true)
                    else (1, true) // a < b
            else (res, decid))
            (0, false) 
    if decided then result else 0

let (.<.) x y = (compare x y) < 0
let (.>.) x y = (compare x y) > 0
let (.<=.) x y = (compare x y) <= 0
let (.>=.) x y = (compare x y) >= 0
let (.=.) x y = (compare x y) = 0


//==================================================

let colours = 
    [|"#8dd3c7";"#fdb462";"#bebada";"#fb8072";"#80b1d3";"#ffffb3";"#b3de69";"#fccde5";"#d9d9d9";"#bc80bd";"#ccebc5";"#ffed6f"|]
let getColour id = 
    colours.[id % colours.Length]


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
      Text = match article.Text with | Some t -> t | None -> []
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
      Loading = true
      }, 
    fetchArticleCmd article user

//------------------------------------------------------------------------------------------------
// Determine if a given position is included in any highlighted region

let isInsideHelper paragraph position (selection : Selection) =    
    if selection.StartParagraphId .=. paragraph && selection.EndParagraphId .=. paragraph then
        position >= selection.StartIdx && position <= selection.EndIdx
    else if selection.StartParagraphId .=. paragraph then 
        position >= selection.StartIdx 
    else if selection.EndParagraphId .=. paragraph then
        position <= selection.EndIdx
    else if selection.IncludedParagraphs |> List.contains paragraph  then
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

//------------------------------------------------------------------------------------------------
// Find all leaf elements in the HTML tree that represents the article - these leaf elements contain text with the highlights

let rec collectArticleElementIds selectStatus (acc : IdAttribute list) startId endId (node : SimpleHtmlNode) =
    match node with 
    | SimpleHtmlText _ -> selectStatus, acc
    | SimpleHtmlElement(name, id, children, isLeaf) ->
        match selectStatus with
        | false ->
            if id .=. startId then    
                ((true, [id]), children)
                ||> List.fold (fun (status, acc') child -> 
                    collectArticleElementIds status acc' startId endId child)
            else                
                ((false, acc), children)
                ||> List.fold (fun (status, acc') child -> 
                    collectArticleElementIds status acc' startId endId child)
        | true ->
            if id .=. endId then
                false, id :: acc // assume already added to accummulator
            else
                ((true, acc), children)
                ||> List.fold (fun (status, acc') child -> 
                    collectArticleElementIds status (if status then id :: acc' else acc') startId endId child)

let rec filterLeafElementIds acc nodeList node =
  match node with
  | SimpleHtmlText _ -> true, acc
  | SimpleHtmlElement(_,id, children, isLeaf) ->
      let _, updatedAcc, _ = 
        ((false, acc, false), children)
        ||> List.fold (fun (state, acc', skipRest) ch -> 
            if skipRest then (state, acc', true)
            else
              let isLeaf, acc'' = filterLeafElementIds acc' nodeList ch
              if isLeaf then 
                if nodeList |> List.contains id then 
                  (false, id::acc'', true)
                else
                  (false, acc'', false)
              else 
                (state, acc'', false)
              )
      false, updatedAcc            

let extractIntermediateLeafElements startId endId nodes =
  if startId = endId then [startId]
  else
      nodes
      |> List.collect (fun node ->
          let _, elements = collectArticleElementIds false [] startId endId node
          let _, leafElements = filterLeafElementIds [] elements node
          leafElements
      ) 


type SelectionResult = 
    | NoSelection
    | NewHighlight of SourceId * Selection
    | ClickHighlight of SourceId * HighlightType * Selection
    

let getSelection (model: Model) e : SelectionResult = 
    let rawOutput = Browser.window.getSelection()
    
    match string (rawOutput?("type")) with
    | "Range" -> 
        match model.SourceSelectionMode with
        | NoHighlight -> NoSelection
        | SourceText(id) | AnonymityText(id) ->
            let startParagraphId = rawOutput.anchorNode.parentElement.id
            let endParagraphId = rawOutput.focusNode.parentElement.id

            let startIdx = rawOutput.anchorOffset |> int
            let endIdx = rawOutput.focusOffset |> int

            // This part deals with selection done in reverse, from back to front
            let startP, startI, endP, endI =
                if startParagraphId .<. endParagraphId then
                    startParagraphId, startIdx, endParagraphId, endIdx
                else if startParagraphId .=. endParagraphId then
                    startParagraphId, min startIdx endIdx, endParagraphId, max startIdx endIdx
                else 
                    endParagraphId, endIdx, startParagraphId, startIdx

            if startI = 0 && endI = 0 then 
                window.getSelection().removeAllRanges()
                NoSelection // This only ignores the issue
                // TODO: deal with the issue properly 
                //      - identify internal leafs correctly
                //      - finish highlight in the previous element instead of 0-th position of the next element
            else

                let result = 
                 (id, { 
                   StartParagraphId = startP
                   StartIdx = startI
                   EndParagraphId = endP
                   EndIdx = endI
                   IncludedParagraphs = extractIntermediateLeafElements startParagraphId endParagraphId model.Text
                   Text = rawOutput.toString() })

                window.getSelection().removeAllRanges()
                result |> NewHighlight
    | "Caret" -> 
        let paragraph = 
            let elementId = rawOutput.anchorNode.parentElement.id 
            //model.Text |> Array.findIndex (fun el -> el.Id = elementId)
            elementId
        let position = rawOutput.anchorOffset |> int

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
            h4 [ ClassName "question"
                 Style [ BorderBottomColor (getColour n) ]  
                 ] 
                 [ str ("Source number " + string (n+1)) ]
            input [ HTMLAttr.Type "text"
                    ClassName "form-control input-md"
                    Placeholder "Notes"
                    DefaultValue (
                        match model.SourceInfo.[n].UserNotes with
                        | Some(note) -> note
                        | None -> "") 
                    Value (
                        match model.SourceInfo.[n].UserNotes with
                        | Some(note) -> note
                        | None -> "") 
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
  

let viewParagraphHighlights (model: Model) (paragraphId: IdAttribute) (text: string) (dispatch: Msg -> unit) =
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
                if paragraphId .>=. selection.StartParagraphId && paragraphId .<=. selection.EndParagraphId then
                    let startI = 
                        if selection.StartParagraphId .=. paragraphId then selection.StartIdx
                        else 0
                    let endI = 
                        if selection.EndParagraphId .=. paragraphId then selection.EndIdx
                        else text.Length       
                    Browser.console.log("highlight start index: " + string startI)
                    Browser.console.log("highlight end index: " + string endI)
                                        
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
            span [ ClassName "span"
                   Style [ BackgroundColor (getColour id) ]
                      ] [ str part.Text ]
        | Some (AnonymityText id) ->
            span [ ClassName "anon"; Style [BorderBottomColor (getColour id)] ] [ str part.Text ]
        | Some (NoHighlight) ->
            str part.Text
        | None -> str part.Text)    

let viewArticleElement  (element: SimpleHtmlNode) (elementType : ArticlePageElementType) model dispatch =

     
    let rec viewArticleElementTree (parentId : IdAttribute option) (htmlElement : SimpleHtmlNode) : ReactElement list option =
        match htmlElement with

        | SimpleHtmlElement(elementName, id, contents, isLeaf) ->
            if translateNameWithContent.ContainsKey elementName then
                // translate the element and call recursively on content
                let name = translateNameWithContent.[elementName]
                let attr : IHTMLProp list = [
                  yield upcast (Id id) 
                  if isLeaf then 
                      // attach event handlers
                      yield upcast
                        (OnMouseUp (fun e -> 
                          match (getSelection model e) with
                          | NewHighlight(id, selection) -> 
                             dispatch (TextSelected (id,selection))
                          | ClickHighlight(id, selectionType, selection) -> 
                             dispatch (ShowDeleteButton (id, selectionType, selection))
                          | NoSelection -> () ) )   ] 
                let body = 
                    contents 
                    |> List.choose (viewArticleElementTree (Some id))
                    |> List.concat
                let result = 
                      ( name attr body ) 
                Some [ result ]

            else 
                // skip the current element and continue with the rest
                if translateNameWithoutContent.ContainsKey elementName then
                    Some [(translateNameWithoutContent.[elementName] [])]
                else 
                    None

        | SimpleHtmlText content -> 
          match elementType with 
          | BaseText ->
                match model.ShowDeleteSelection, parentId with 
                | Some (id, selectionType, selection), Some parent ->
                  if selection.EndParagraphId <> parent then
                    Some [ str content ]
                  else 
                    Browser.console.log("inserting delete button, id = " + parent)
                    let part1 = content.[..selection.EndIdx-1]
                    let part2 = content.[selection.EndIdx..]
                    let result = [
                        yield span [] [ str part1 ]
                        yield button [ 
                               ClassName "btn btn-danger delete-highlight-btn" 
                               OnClick (fun _ -> dispatch (DeleteSelection (id, selectionType, selection)))]
                               [ str ("╳ Source " + string (id + 1)) ]
                        yield span [] [ str part2 ]
                    ]
                    Some result      
                | _ -> 
                    Some [ str content ]
                        

          | HighlightedText -> 
              match parentId with
              | Some parent ->
                  Some (viewParagraphHighlights model parent content dispatch)
              | None ->
                  Browser.console.log("Highlighted text with no parent ID.")
                  None // error?   
              


    viewArticleElementTree None element


let view (model:Model) (dispatch: Msg -> unit) : ReactElement list =
  [
   (if model.Loading then
       div [] [
         div [ ClassName "body" ] [
             img [ HTMLAttr.Src "Images/Double Ring-1.7s-200px.gif" ] 
         ]
    //            h5 [] [ str "Loading articles..."]
         div [ ClassName "bottom" ] [ a [ HTMLAttr.Href "https://loading.io/spinner/double-rignt/"] [ str "Spinner by loading.io"] ]
       ]
     else

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
                    // text with highlights at the bottom
                    ( model.Text 
                     |> List.choose (fun element -> viewArticleElement element HighlightedText model dispatch)
                     |> List.concat
                      )
                  div [ ClassName "article-text container col-sm-12" ] 
                    // text without highlights and for highlighting
                    ( model.Text 
                     |> List.choose (fun element -> viewArticleElement element BaseText model dispatch)
                     |> List.concat
                      )
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
                          
                     yield
                       div [ClassName "row"] [     
                         button 
                          [ ClassName "btn btn-primary"
                            OnClick (fun _ -> dispatch (AddSource (model.SourceInfo.Length))) ]
                          [ str "+ Add additional source"]
                       ]
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
     )
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
                        Submitted = Some true
                        Loading = false }
                    |> isCompleted, Cmd.none, NoOp
                else
                    Browser.console.log("Annotations loaded for incorrect user.")
                    { model with Text = t; Loading = false }|> isCompleted, Cmd.none, NoOp
            | None -> 
                { model with Text = t; Loading = false }|> isCompleted, Cmd.none, NoOp
        | None -> { model with Loading = false } |> isCompleted, Cmd.none, NoOp

    | FetchError e ->
        { model with Loading = false } |> isCompleted, Cmd.none, NoOp

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
        { model with Loading = true }, Cmd.none, GetNextArticle

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

        { model with SourceInfo = sources } |> isCompleted, Cmd.none, NoOp
