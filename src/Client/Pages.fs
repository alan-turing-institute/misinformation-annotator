module Client.Pages

open Elmish.Browser.UrlParser

/// The different pages of the application. If you add a new page, then add an entry here.
[<RequireQualifiedAccess>]
type Page =
    | Home
    | Login
    | Annotations
    | Article

let toPath =
    function
    | Page.Home -> "/"
    | Page.Login -> "/login"
    | Page.Annotations -> "/annotations"
    | Page.Article -> "/article"


/// The URL is turned into a Result.
let pageParser : Parser<Page -> Page,_> =
    oneOf
        [ map Page.Home (s "")
          map Page.Login (s "login")
          map Page.Annotations (s "annotations")
          map Page.Article (s "article") ]

let urlParser location = parsePath pageParser location
