module Client.Home

open Fable.Helpers.React
open Fable.Helpers.React.Props
open Style
open Pages
open Client

let view () =
    [
      h1 [] [ str "Misinformation project"]
      h2 [] [ str "Annotation tool" ]
      a [ Href "https://turing.ac.uk"] [ words 15 "The Alan Turing Institute" ] ]
