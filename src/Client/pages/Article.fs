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

let init (article: Article) = 
    { Article = Some article; Tags = [] }, Cmd.none

type Msg = 
    | View

type ExternalMsg = 
    | DisplayArticle of Article
    | NoOp

let view (model:Model) (dispatch: Msg -> unit) =
    [ words 60 (
        match model.Article with
        | Some a ->
            sprintf "Title: %s" a.Title
        | None -> 
            "No article selected") ]

let update (msg:Msg) model : Model*Cmd<Msg>*ExternalMsg =
    match msg with
    | View -> 
        match model.Article with 
        | Some a -> model, Cmd.none, DisplayArticle a
        | None -> model, Cmd.none, NoOp



