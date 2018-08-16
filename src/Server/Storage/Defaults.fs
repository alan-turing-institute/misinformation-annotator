module ServerCode.Storage.Defaults

open ServerCode.Domain

/// The default initial data 
let defaultAnnotations userName =
    { UserName = userName
      Articles = 
        [ { Title = "Mastering F#"
            ID = "https://www.amazon.com/Mastering-F-Alfonso-Garcia-Caro-Nunez-earticle/dp/B01M112LR9"
            Text = None }
          { Title = "Get Programming with F#"
            ID = "https://www.manning.com/articles/get-programming-with-f-sharp" 
            Text = None } ] }