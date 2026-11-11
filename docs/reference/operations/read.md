# `read`

Reads one line of input and returns it as a string, without the line break.

| Operation | Arity | Arguments | Result |
| --- | --- | --- | --- |
| `read` | 0 | none | `string` |

```pyrop
write "Name: "
define string name read
write_line add "Hello " name
```

```input
Ann
```

```output
Name: 
Hello Ann
```

A piped answer is not shown, but Pyrop ends the line the prompt left open, so the next output starts on a line of its own, as after Enter on a terminal. Reading past the end of input is a runtime error. Combine `read` with `convert` to read numbers:

```pyrop
write "Age: "
define int age convert int read
write_line fill "Next year you will be {}." list_of int add age 1 end
```

```input
41
```

```output
Age: 
Next year you will be 42.
```
