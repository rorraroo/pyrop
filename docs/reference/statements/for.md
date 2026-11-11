# `for`

Runs a statement once for every item of a list, in order. The current item is in the loop variable.

```
for <type> <name> in <list> <statement>
```

```pyrop
define list string inventory list_of string "lamp" "rope" end
for string item in inventory write_line item

define map string int prices map_of string int "lamp" 5 "rope" 12 end
for string name in keys_of prices block
    write add name ": "
    write_line get prices name
end
```

```output
lamp
rope
lamp: 5
rope: 12
```

- `for` walks lists only. To walk a map, walk its keys with `keys_of`. To know each item's position, walk the positions with `for int index in positions_of items` and read the item with `get items index`.
- The loop variable's type must be the list's item type exactly: a `list int` is walked with `for int …`.
- The loop variable exists only inside the loop, afresh for each item. `set` may change it for the rest of that item's run.
- The list is taken once, when the loop starts. Changing the list's variable inside the body does not change which items the loop visits.
- The loop variable cannot reuse the name of a variable declared earlier in the same block, or of a parameter when the `for` is the function's body or sits directly in its `block`. Inside an inner block it may hide an outer name.
- A `define` with the loop variable's name directly in the body's `block` is an error. The body is a single statement, and cannot be a bare `define`.
- `break` and `continue` work as in `while`.
- To count from one number to another, use `while` with an `int` variable.
