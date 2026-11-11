# `choose` and `write_table`

Show a menu or a table.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `choose` | 2 | `string` prompt, `list string` entries | `string` | `choose "Where to?" rooms` → the picked entry |
| `write_table` | 2 | `list string` headers, `list list string` rows | statement | `write_table headers rows` |

```pyrop
define string room choose "Where do you go?" list_of string "Hall" "Cellar" end
write_table list_of string "Item" "Count" end list_of list string
    list_of string "lamp" "1" end
    list_of string "rope" "12" end
end
```

```input
2
```

```output
Where do you go?
1. Hall
2. Cellar
Choose a number from 1 to 2: 
Item  Count
lamp  1
rope  12
```

- On a terminal, `choose` shows a list you move through with the arrow keys and pick with Enter. Afterwards the prompt and the picked entry stay on one line.
- In plain mode, or when input is piped, `choose` prints the prompt, the entries numbered from 1 and `Choose a number from 1 to N: `, then reads a number. When the answer is piped it is not shown, but the line is ended, so what comes next starts on a line of its own. Any other answer prints a warning and asks again. Use `--plain` (or set `NO_COLOR`) with a screen reader to always get this form.
- An empty list and running out of input are runtime errors; `attempt choose` gives `none` for both.
- On a terminal, `write_table` draws a table with borders. When the terminal is too narrow for it, the plain form is printed instead. In plain mode it pads every column but the last with spaces and puts two spaces between columns:

```
Item  Count
lamp  1
rope  12
```

- Every row must have as many cells as there are headers; a row that does not is a runtime error naming the row, numbered from 0. Nothing is printed when the table has an error.
- `styled` applies to both.
