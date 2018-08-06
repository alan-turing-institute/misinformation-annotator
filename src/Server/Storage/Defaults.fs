module ServerCode.Storage.Defaults

open ServerCode.Domain

/// The default initial data 
let defaultAnnotations userName =
    { UserName = userName
      Articles = 
        [ { Title = "Mastering F#"
            Link = "https://www.amazon.com/Mastering-F-Alfonso-Garcia-Caro-Nunez-earticle/dp/B01M112LR9" }
          { Title = "Get Programming with F#"
            Link = "https://www.manning.com/articles/get-programming-with-f-sharp" } ] }