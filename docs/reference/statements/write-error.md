# `write_error` and `write_error_line`

Print a value to standard error instead of standard output, for usage messages, warnings and other diagnostics. They work exactly like `write` and `write_line`.

```
write_error <expression>
write_error_line <expression>
```

```pyrop
use program library

if equal length_of from program arguments 0 block
    write_error_line "usage: filter <file>"
    exit 2
end
```

```output
usage: filter <file>
```

- When the program's output is redirected to a file or a pipe, error text still appears on the terminal and never ends up in the file.
- `styled` works on them. Styles show when standard error is a terminal, and are dropped when standard error is redirected, `NO_COLOR` is set or `--plain` is given.
- If the program then stops with an error, Pyrop's message starts on a new line after the program's error text.
