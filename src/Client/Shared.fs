module Client.Shared

open ServerCode.Domain
/// The composed model for the different possible page states of the application
type PageModel =
    | HomePageModel
    | LoginModel of Login.Model
    | AnnotationsModel of Annotations.Model
    | ArticleModel of Article.Model

/// The composed model for the application, which is a single page state plus login information
type Model =
    { User : UserData option
      SelectedArticle : Article option
      AllArticles : ArticleList option
      PageModel : PageModel }

/// The composed set of messages that update the state of the application
type Msg =
    | LoggedIn of UserData
    | LoggedOut
    | StorageFailure of exn
    | LoginMsg of Login.Msg
    | AnnotationsMsg of Annotations.Msg
    | Logout of unit
    | ArticleMsg of Article.Msg
   

// VIEW

open Fable.Helpers.React
open Fable.Helpers.React.Props
open Client.Style

/// Constructs the view for a page given the model and dispatcher.
let viewPage model dispatch =
    match model.PageModel with
    | HomePageModel ->
        Home.view ()

    | LoginModel m ->
        [ Login.view m (LoginMsg >> dispatch) ]

    | AnnotationsModel m ->
        [ Annotations.view m (AnnotationsMsg >> dispatch) ]

    | ArticleModel m ->
        Article.view m (ArticleMsg >> dispatch)


/// Constructs the view for the application given the model.
let view model dispatch =
    div [] [
        Menu.view (Logout >> dispatch) model.User
        hr []
        div [ centerStyle "column" ] (viewPage model dispatch)
    ]
