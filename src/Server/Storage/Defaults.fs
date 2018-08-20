module ServerCode.Storage.Defaults

open ServerCode.Domain

/// The default initial data 
let defaultAnnotations userName =
    { Title = ""
      ID = ""
      Annotations = [||] }


let defaultArticles  userName =
    { UserName = userName
      Articles = 
        [  ] }