# 5. Lists and maps

## Lists

A list holds any number of values of one type, in order. `list string` is the type of a list of strings, and `list_of string … end` makes one:

```pyrop
define list string bag list_of string "lamp" "rope" end
write_line bag
write_line length_of bag
write_line get bag 0
```

```output
["lamp", "rope"]
2
lamp
```

Positions are numbered from 0, so `get bag 0` is the first item. An empty list still names its type: `list_of string end`.

## Lists never change, variables do

`push` gives a new list with one more item at the end. The old list stays as it was, so to change what a variable holds, `set` it:

```pyrop
define list string bag list_of string "lamp" end
set bag push bag "key"
write_line bag
```

```output
["lamp", "key"]
```

This is true of every value in Pyrop. When you pass a list to a function, the function cannot change your list; it can only give back a new one.

| Operation | Gives |
| --- | --- |
| `push list item` | the list with the item added at the end |
| `put list position item` | the list with the item at that position replaced |
| `remove list position` | the list without the item at that position |
| `has list item` | whether the item is in the list |
| `get list position` | the item at that position |
| `slice list start count` | `count` items from position `start` on |
| `length_of list` | how many items there are |

```pyrop
define list int scores list_of int 12 7 30 end
set scores put scores 1 9
set scores remove scores 0
write_line scores
write_line has scores 30
```

```output
[9, 30]
true
```

## Going through a list

`for` visits every item in order:

```pyrop
define list int scores list_of int 12 7 30 end
define int total 0
for int score in scores set total add total score
write_line total
```

```output
49
```

When you need to know where each item is, go through its positions instead. `positions_of` gives them as a list, `[0, 1, 2]` for a list of three:

```pyrop
define list int scores list_of int 12 7 30 end
for int position in positions_of scores
    if greater get scores position 10 write_line fill "Score {} is above 10." list_of int position end
```

```output
Score 0 is above 10.
Score 2 is above 10.
```

## Sorting a list

`sort` puts a list of numbers or texts in order, smallest first, `reverse` turns a list around, and `distinct` leaves out repeats. Each gives you a new list, like `push` does:

```pyrop
define list int scores list_of int 12 7 30 7 end
write_line sort scores
write_line reverse sort scores
write_line distinct scores
```

```output
[7, 7, 12, 30]
[30, 12, 7, 7]
[12, 7, 30]
```

## Lists from text and text from lists

`split` cuts a text at a separator, and `join` puts a list of texts back together:

```pyrop
define list string words split "red,green,blue" ","
write_line length_of words
write_line join words " and "
```

```output
3
red and green and blue
```

## Lists of lists

Types nest without brackets: `list list int` is a list whose items are lists of ints.

```pyrop
define list list int grid list_of list int
    list_of int 1 2 end
    list_of int 3 4 end
end
write_line get get grid 1 0
```

```output
3
```

## Maps

A map connects keys to values. `map string int` has text keys and int values:

```pyrop
define map string int prices map_of string int "lamp" 5 "rope" 2 end
write_line get prices "lamp"
set prices put prices "key" 8
write_line prices
```

```output
5
{"lamp": 5, "rope": 2, "key": 8}
```

`put` adds a key or replaces its value, `remove` takes a key out, `has` tells whether a key is there, and `keys_of` lists the keys in the order they were added, which is how you go through a map:

```pyrop
define map string int prices map_of string int "lamp" 5 "rope" 2 end
for string item in keys_of prices
    write_line fill "{} costs {}" list_of string item convert string get prices item end
```

```output
lamp costs 5
rope costs 2
```

Asking `get` for a position or a key that is not there stops the program with an error. Chapter 8 shows how to ask safely.
