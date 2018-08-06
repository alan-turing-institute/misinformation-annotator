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

/// The data for each article in /api/annotations
type Article =
    { Title: string
      Link: string }

    static member empty =
        { Title = ""
          Link = "" }

/// The logical representation of the data for /api/annotations
type Annotations =
    { UserName : string
      Articles : Article list }

    // Create a new Annotations.  This is supported in client code too,
    // thanks to the magic of https://www.nuget.org/packages/Fable.JsonConverter
    static member New userName =
        { UserName = userName
          Articles = [] }

type AnnotationsResetDetails =
    { Time : DateTime }
