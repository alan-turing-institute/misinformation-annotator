module ServerTests.Tests

open Expecto
open ServerCode
open ServerCode.Storage


let AnnotationsTests =
  testList "Annotations" [
    (*
    testCase "default contains F# mastering article" <| fun _ ->
      let defaults =  ServerCode.Storage.Defaults.defaultAnnotations "test"
      Expect.isNonEmpty defaults.Articles "Default Articles list should have at least one item"
      Expect.isTrue
        (defaults.Articles |> Seq.exists (fun b -> b.Title = "Mastering F#")) 
        "A good article should have been advertised"
    *)        
    ]

