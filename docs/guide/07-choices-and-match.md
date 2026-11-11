# 7. Choices and `match`

A choice is a type with a fixed set of possible values, called its variants. A direction is one of four, a traffic light has three colours, a player's action is one of a few kinds.

## Declaring a choice

`choice` takes a name and its variants, up to `end`:

```pyrop
choice Direction north east south west end

define Direction facing north
write_line facing
write_line equal facing south
```

```output
north
false
```

A variant is a value on its own: `north` is a `Direction`. Using choices instead of strings such as `"north"` means a misspelt direction is found before the program runs.

`variants_of` gives every variant as a list, in the order they were declared. A menu or loop built from it stays complete when you add a variant:

```pyrop
choice Direction north east south west end

for Direction way in variants_of Direction write_line way
```

```output
north
east
south
west
```

## Variants that carry data

A variant can carry values, listed by type after its name. Then it takes them like a function takes arguments:

```pyrop
choice Direction north east south west end
choice Action go Direction take string wait end

define Action next go north
write_line next
set next take "lamp"
write_line next
```

```output
go north
take "lamp"
```

`go` carries a `Direction`, so `go north` is one `Action`. `take` carries a `string`. `wait` carries nothing.

## Handling each variant: `match`

`match` runs the branch for the variant a value holds. Each branch starts with `when`, the variant, and a name for each value it carries:

```pyrop
choice Direction north east south west end
choice Action go Direction take string wait end

function void perform Action action
    match action
        when go way write_line fill "You go {}." list_of Direction way end
        when take item write_line fill "You take the {}." list_of string item end
        when wait write_line "You wait."
    end

perform go east
perform take "rope"
perform wait
```

```output
You go east.
You take the rope.
You wait.
```

Every variant needs its `when`. Leave one out and Pyrop says so before running, so when you add a variant later, it shows you every `match` that must learn about it:

```pyrop
choice Light red amber green end

function string advice Light light
    match light
        when red return "Stop"
        when green return "Go"
    end
```

```error
This match does not handle the variant amber of Light; a match needs a when for every variant.
```

## Choices from text

`convert` reads a variant from its name, which is handy for input:

```pyrop
choice Direction north east south west end
define Direction way convert Direction "west"
write_line way
```

```output
west
```

A text that names no variant stops the program with an error listing the variants. The next chapter shows how to handle such a mistake instead.

Next: [When something can go wrong: options and `attempt`](08-options-and-attempt.md)
