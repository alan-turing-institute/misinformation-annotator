module Client.HtmlElements

open Fable.Helpers.React
open Fable.Helpers.React.Props
open ServerCode.Domain

let translateNameWithoutContent =
    [
        ("area", area)
        ("base", ``base``)
        ("br", br)
        ("col", col)
        ("embed", embed)
        ("hr", hr)
        ("img", img)
        ("input", input)
        ("keygen", keygen)
        ("link", link)
        ("menuitem", menuitem)
        ("meta", meta)
        ("param", param)
        ("source", source)
        ("track", track)
        ("wbr", wbr)
    ] |> dict

let translateNameWithContent = 
    [
        ("h1", h1)
        ("a", a)
        ("abbr", abbr)
        ("address", address)
        ("article", article)
        ("aside", aside)
        ("audio", audio)
        ("b", b)
        ("bdi", bdi)
        ("bdo", bdo)
        ("big", big)
        ("blockquote", blockquote)
        ("body", body)
        ("button", button)
        ("canvas", canvas)
        ("caption", caption)
        ("cite", cite)
        ("code", code)
        ("colgroup", colgroup)
        ("data", data)
        ("datalist", datalist)
        ("dd", dd)
        ("del", del)
        ("details", details)
        ("dfn", dfn)
        ("dialog", dialog)
        ("div", div)
        ("dl", dl)
        ("dt", dt)
        ("em", em)
        ("fieldset", fieldset)
        ("figcaption", figcaption)
        ("figure", figure)
        ("footer", footer)
        ("form", form)
        ("h1", h1)
        ("h2", h2)
        ("h3", h3)
        ("h4", h4)
        ("h5", h5)
        ("h6", h6)
        ("head", head)
        ("header", header)
        ("hgroup", hgroup)
        ("html", html)
        ("i", i)
        ("iframe", iframe)
        ("ins", ins)
        ("kbd", kbd)
        ("label", label)
        ("legend", legend)
        ("li", li)
        ("main", main)
        ("map", map)
        ("mark", mark)
        ("menu", menu)
        ("meter", meter)
        ("nav", nav)
        ("noscript", noscript)
        ("object", ``object``)
        ("ol", ol)
        ("optgroup", optgroup)
        ("option", option)
        ("output", output)
        ("p", p)
        ("picture", picture)
        ("pre", pre)
        ("progress", progress)
        ("q", q)
        ("rp", rp)
        ("rt", rt)
        ("ruby", ruby)
        ("s", s)
        ("samp", samp)
        ("script", script)
        ("section", section)
        ("select", select)
        ("small", small)
        ("span", span)
        ("strong", strong)
        ("style", style)
        ("sub", sub)
        ("summary", summary)
        ("sup", sup)
        ("table", table)
        ("tbody", tbody)
        ("td", td)
        ("textarea", textarea)
        ("tfoot", tfoot)
        ("th", th)
        ("thead", thead)
        ("time", time)
        ("title", title)
        ("tr", tr)
        ("u", u)
        ("ul", ul)
        ("var", var)
        ("video", video)
    ] 
    |> dict

let translateNameWithContent' =
    [        
        ("svg", svg)
        ("circle", circle)
        ("defs", defs)
        ("ellipse", ellipse)
        ("g", g)
        ("image", image)
        ("line", line)
        ("linearGradient", linearGradient)
        ("mask", mask)
        ("path", path)
        ("pattern", pattern)
        ("polygon", polygon)
        ("polyline", polyline)
        ("radialGradient", radialGradient)
        ("rect", rect)
        ("stop", stop)
        ("text", text)
        ("tspan", tspan)
    ] |> dict


let rec parseAllElements (element: SimpleHtmlNode) : Fable.Import.React.ReactElement option =
    match element with
    | SimpleHtmlElement(elementName, id, contents) ->
        if translateNameWithContent.ContainsKey elementName then
            // translate the element and call recursively on content
            let name = translateNameWithContent.[elementName]
            let attr : IHTMLProp list = [ Id id ] 
            let body = 
                contents 
                |> List.choose parseAllElements
            let result = 
                  ( name attr body ) 
            Some result 

        else 
            // skip the current element and continue with the rest
            if translateNameWithoutContent.ContainsKey elementName then
                Some (translateNameWithoutContent.[elementName] [])
            else 
                None

    | SimpleHtmlText x -> Some (str x) // return the text

let htmlToReact (contents : SimpleHtmlNode list) =
    contents
    |> List.choose parseAllElements