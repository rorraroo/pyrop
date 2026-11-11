# `block` … `end`

Groups several statements into one.

```
block <statement> <statement> … end
```

```pyrop
define int age 30
define bool allowed false
if greater age 18 block
    write_line "Adult"
    set allowed true
end
```

```output
Adult
```

A block also creates a new scope. Variables declared inside it disappear at `end`, and a loop body that is a block starts with fresh variables on every iteration.

**When to write `end`.** A form ends with `end` only when Pyrop cannot know in advance how many parts it has. If the parts are statements, that form is `block … end`; otherwise the form starts with its own keyword:

| Parts | Form |
| --- | --- |
| statements | `block … end` |
| list items | `list_of int 1 2 3 end` |
| map entries | `map_of string int "lamp" 5 end` |
| fields | `record Point int x int y end` |
| variants | `choice Direction north south end` |
| changed fields | `copy point with x 1 with y 2 end` |
| imported names | `import from world Place Room end` |
| branches | `match action when go way … when wait … end` |

Everything else takes a fixed number of parts and has no `end`, apart from two lists that end at the first word that is not a type: a function's parameters, because its body always follows, and the data a variant carries, inside its `choice … end`. A body of `if`, `while`, `for`, `when` or `function` is one statement, which is why several statements need a `block`; a list of items never does.
