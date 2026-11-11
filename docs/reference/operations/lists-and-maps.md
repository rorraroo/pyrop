# Lists and maps

Write a collection out with `list_of` or `map_of`:

```
map_of <key type> <value type> <key> <value> … end
list_of <element type> <expression> … end
```

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `length_of` | 1 | `list T` or `map K V` | `int` | `length_of list_of int 4 5 end` → `2` |
| `get` | 2 | `list T` list, `int` index | `T` | `get list_of string "a" "b" end 1` → `"b"` |
| `push` | 2 | `list T` list, `T` item | `list T` | `push list_of int 1 end 2` → `[1, 2]` |
| `put` | 3 | `list T` list, `int` index, `T` item | `list T` | `put list_of int 1 2 end 0 9` → `[9, 2]` |
| `remove` | 2 | `list T` list, `int` index | `list T` | `remove list_of int 1 2 end 0` → `[2]` |
| `has` | 2 | `list T` list, `T` item | `bool` | `has list_of int 1 2 end 2` → `true` |
| `slice` | 3 | `list T` list, `int` start, `int` count | `list T` | `slice list_of int 1 2 3 4 end 1 2` → `[2, 3]` |
| `positions_of` | 1 | `list T` | `list int` | `positions_of list_of string "a" "b" end` → `[0, 1]` |
| `sort` | 1 | `list int`, `list float` or `list string` | the same list type | `sort list_of int 3 1 2 end` → `[1, 2, 3]` |
| `reverse` | 1 | `list T` | `list T` | `reverse list_of int 1 2 3 end` → `[3, 2, 1]` |
| `distinct` | 1 | `list T` | `list T` | `distinct list_of int 1 2 1 end` → `[1, 2]` |
| `get` | 2 | `map K V` map, `K` key | `V` | `get prices "lamp"` → `5` |
| `put` | 3 | `map K V` map, `K` key, `V` value | `map K V` | `put prices "rope" 2` |
| `remove` | 2 | `map K V` map, `K` key | `map K V` | `remove prices "rope"` |
| `has` | 2 | `map K V` map, `K` key | `bool` | `has prices "lamp"` → `true` |
| `keys_of` | 1 | `map K V` | `list K` | `keys_of prices` → `["lamp", "rope"]` |
| `values_of` | 1 | `map K V` | `list V` | `values_of prices` → `[5, 2]` |

```pyrop
define list string inventory list_of string "lamp" "rope" end
set inventory push inventory "key"
write_line inventory                          # ["lamp", "rope", "key"]

define map string int prices map_of string int "lamp" 5 "rope" 2 end
set prices put prices "torch" 8
if has prices "lamp" write_line get prices "lamp"
```

```output
["lamp", "rope", "key"]
5
```

- **Operations never change a collection; they return a new one.** Write a change with `set`: `set inventory push inventory "lamp"`. A copy made with `define` or passed to a function is unaffected by later changes to the original, and the other way round.
- **Walk a list** with [`for`](../statements/for.md), and a map with `for` over `keys_of`, or over `values_of` when only the values matter; both give the entries in the order they were added. To know where each item is, walk `positions_of` the list and `get` each item.
- **`slice <list> <start> <count>`** takes `count` items from position `start`, as it takes characters from a [text](text.md). A start outside 0 to `length_of` the list, a negative count, or a range past the end is a runtime error.
- **`sort <list>`** puts the items from smallest to largest, in the order [`less`](comparison.md) gives. Strings are compared by character code, so the result is the same on every machine, and uppercase letters come before lowercase ones. Only lists of `int`, `float` or `string` can be sorted. To go through a map in key order, use `sort keys_of` the map.
- **`reverse <list>`** puts the items in the opposite order. For largest first, write `reverse sort` the list.
- **`distinct <list>`** leaves out every item that is `equal` to an earlier one, and keeps the rest in their order.

```pyrop
define list string log list_of string "You enter." "A rat bites." "You hit the rat." "The rat flees." "You find 5 gold." end
define int shown smaller_of 3 length_of log
for string line in slice log subtract length_of log shown shown write_line line

define list string pack list_of string "lamp" "rope" "key" end
for int index in positions_of pack
    write_line fill "{}. {}" list_of string convert string add index 1 get pack index end
```

```output
You hit the rat.
The rat flees.
You find 5 gold.
1. lamp
2. rope
3. key
```

- **`list_of` and `map_of` end at `end`**, like `block`, so their items may span several lines. `list_of int end` is an empty list and `map_of string int end` an empty map. Every item must have the element type, and in a `map_of` each key is followed by its value, with every key of the key type and every value of the value type. A key written twice keeps its first place and its last value.
- **Map keys** must be `int`, `string`, `bool` or a [choice](../statements/choices.md). `put` adds a key or replaces its value.
- **Order:** maps remember the order in which keys were first put. Replacing a value keeps the key's place; removing a key and putting it again moves it to the end.
- **Runtime errors:** an index outside the list (indexes go from 0 to `length_of` minus 1), a `slice` that does not fit in the list, `get` or `remove` with a key that is not in the map (check with `has` first), and a collection with more than 10,000,000 items.
- **`equal`** compares collections by content. Two maps are equal when they hold the same keys and values, in any order.
- **Printing** a collection, or converting it with `convert string`, shows `["lamp", "key"]` or `{"lamp": 5}`. Strings inside a collection are quoted, with `\"`, `\\`, `\n`, `\r` and `\t` escaped.
