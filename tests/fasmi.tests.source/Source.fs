module Source

let inc x = x+1

let jump x =
    if x >= 0 then
        x
    else
        x * 3 + 2

let toString (x: int) =
    x.ToString()
