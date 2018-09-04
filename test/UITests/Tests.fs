module UITests.Tests

open canopy
open canopy.classic
open canopy.types
open System.IO
open Expecto
open System
open System.Threading

let serverUrl = "http://localhost:8085/"
let logoutLinkSelector = ".logout"
let loginLinkSelector = "Login"
let username = "test"
let password = "test"

let startApp () =
    url serverUrl
    waitForElement(".elmish-app")

let login () =
    let logout = someElement logoutLinkSelector 
    if logout.IsSome then
        click logoutLinkSelector 
        waitForElement loginLinkSelector

    click loginLinkSelector

    "#username" << username
    "#password" << password

    click "Log In"
    waitForElement logoutLinkSelector

let logout () =
    click logoutLinkSelector

let tests = 
    testList "client tests" [
        testCase "sound check - server is online" (fun () ->
            startApp ()
        )

        testCase "login with test user" (fun () ->
            startApp ()
            login ()
            logout ()
        )

        (*
        testCase "validate form fields" (fun () ->
            startApp ()
            login ()

            click ".btn"

            element "No title was entered"
            element "No author was entered"
            element "No link was entered"

            "input[name=Title]" << "title"
            let titleWarnElem = someElement "No title was entered"
            Expect.isNone titleWarnElem "should dismiss title warning"

            "input[name=Author]" << "author"
            let authorWarnElem = someElement "No author was entered"
            Expect.isNone authorWarnElem "should dismiss author warning"

            "input[name=Link]" << "link"
            let linkWarnElem = someElement "No link was entered"
            Expect.isNone linkWarnElem "should dismiss link warning"

            logout()
        )

        testCase "create and remove article" (fun () ->
            startApp ()
            login ()

            let initArticleRows =
                match someElement "table tbody tr" with
                | Some (_) -> elements "table tbody tr"
                | None -> []

            let articleTitle = "Expert F# 4.0"
            let articleAuthor = "Don Syme & Adam Granicz & Antonio Cisternino"
            let articleLink = "https://www.amazon.com/Expert-F-4-0-Don-Syme/dp/1484207416"

            "input[name=Title]" << articleTitle
            "input[name=Author]" << articleAuthor
            "input[name=Link]" << articleLink

            click ".btn"

            let titleElement = element articleTitle
            let authorElement = element articleAuthor
            let removeBtn = titleElement |> parent |> parent |> elementWithin "Remove"

            let href = titleElement.GetAttribute("href")
            Expect.equal href articleLink "title element's href should be article link"

            let currArticleRows = elements "table tbody tr"
            Expect.equal currArticleRows.Length (initArticleRows.Length + 1) "should add a new article"

            let articleRemoved () =
                match someElement articleTitle with
                | Some (_) -> false
                | None -> true

            click removeBtn
            waitFor articleRemoved

            let currArticleRows = elements "table tbody tr"
            Expect.equal currArticleRows.Length initArticleRows.Length "should remove the new article"

            logout ()
            
        )
        *)
    ]
