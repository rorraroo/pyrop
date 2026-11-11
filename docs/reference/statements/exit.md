# `exit`

Ends the program immediately with an exit code, which tells the shell or calling program whether it succeeded.

```
exit <int expression>
```

```pyrop
use program library

if equal length_of from program arguments 0
block
    write_error_line "Usage: report <file>"
    exit 2
end
```

```output
Usage: report <file>
```

- **The code must be between 0 and 255.** By convention, `0` means success and anything else means failure. A code outside that range is a runtime error.
- **It ends the whole program**, wherever it is: inside a loop, inside a function, even inside a function called in the middle of an expression. Nothing after it runs.
- Output written before `exit` is kept.
- A function whose path ends in `exit` does not need a `return` on that path:

  ```
  function int parse_or_quit string text
      if equal text "" exit 1
      else return convert int text
  ```
- In the REPL, `exit` closes the REPL with that code.
- A program that ends without `exit` finishes with code `0`, or `1` if an error stopped it.
