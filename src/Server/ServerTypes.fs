/// Module of server domain types.
module ServerCode.ServerTypes

/// Represents the rights available for a request
type UserRights =
   { UserName : string }

type Article =
    {
        Link : string
    }