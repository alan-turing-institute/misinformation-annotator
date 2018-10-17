/// API urls shared between client and server.
module ServerCode.ServerUrls

[<RequireQualifiedAccess>]
module PageUrls =
  [<Literal>]
  let Home = "/"

[<RequireQualifiedAccess>]
module APIUrls =

  [<Literal>]
  let Annotations = "/api/annotations/"
  [<Literal>]
  let ResetTime = "/api/annotations/resetTime/"
  [<Literal>]
  let Login = "/api/users/login/"
  [<Literal>]
  let Article = "/api/article/"
  [<Literal>]
  let Answers = "/api/answers/"
  [<Literal>]
  let ArticleError = "/api/article_err/"