/// Domain model shared between client and server.
namespace ServerCode.Domain

open System

// Json web token type.
type JWT = string

// Login credentials.
type Login =
    { UserName   : string
      Password   : string
      PasswordId : Guid }

    member this.IsValid() =
        not ((this.UserName <> "test"  || this.Password <> "test") &&
             (this.UserName <> "test2" || this.Password <> "test2"))

type UserData =
  { UserName : string
    Token    : JWT }

type ArticleText = string []

/// The data for each article in /api/annotations
type Article =
    { Title: string
      ID: string 
      Text: ArticleText option } // maybe can remove text from here

    static member empty =
        { Title = ""
          ID = "" 
          Text = None }                    

/// The logical representation of the data for /api/annotations
type Annotated = bool
type ArticleList =
    { UserName : string
      Articles : (Article * Annotated) list }

    // Create a new Annotations.  This is supported in client code too,
    // thanks to the magic of https://www.nuget.org/packages/Fable.JsonConverter
    static member New userName =
        { UserName = userName
          Articles = [] }

type AnnotationsResetDetails =
    { Time : DateTime }

type Selection = {
    StartParagraphIdx: int
    EndParagraphIdx : int
    StartIdx : int  // within parent paragraph
    EndIdx: int     // within parent paragraph
    Text: string
}

type SourceId = int

type AnonymousInfo = 
    | NoReasonGiven
    | Reason 

type SourceType = 
    | Named
    | Anonymous

// Information regarding information sources from articles
type SourceInfo = {
    TextMentions : Selection list
    SourceID : SourceId
    SourceType : SourceType option
    AnonymousInfo : AnonymousInfo option
    AnonymityReason : (Selection list) option
}

type ArticleAnnotations =
  { 
    User: UserData
    Title: string
    ArticleID: string // article ID / link
    Annotations: SourceInfo []
    MinutesSpent: float }

type AnswersResponse = 
  { Success : bool }

//===========================================================

type OpenGraphNS = {
    OG: string
}

type OpenGraphData = {
    Namespace: OpenGraphNS
    Properties: string [] []
}

type MicroformatMetadata = {
    OpenGraph : OpenGraphData []
    Microdata : string []
    JsonLd : string []
    Microformat : string []
    Rdfa : obj []
}

type ArticleDBData = {
    SiteName : string
    ArticleUrl : string
    MicroformatMetadata : MicroformatMetadata
    Content : string []
}