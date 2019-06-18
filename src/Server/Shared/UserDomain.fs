


type UserType = 
    | User
    | Expert

type UserProficiency =
    | Standard of UserType
    | Training of UserType
    | Evaluation of UserType

