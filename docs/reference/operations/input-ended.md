# `input_ended`

Tells whether all input has been read, so a program can read everything it is given without an error.

| Operation | Arity | Arguments | Result |
| --- | --- | --- | --- |
| `input_ended` | 0 | none | `bool` |

```pyrop
define int count 0
while not input_ended
block
    read
    set count add count 1
end
write_line count
```

```input
first
second
third
```

```output
3
```

- `input_ended` looks ahead at the next line without using it up, so the following `read` still returns that line.
- When you type at the terminal, `input_ended` waits until you enter a line or signal the end of input (Ctrl+Z then Enter on Windows, Ctrl+D on Linux and macOS).
