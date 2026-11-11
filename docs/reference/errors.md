# Errors

Every error shows its kind, the file and position, a message, and the offending line with a marker:

```
Type error hello.prp:3:12 'add' cannot take int float. It accepts: int int, float float, string string.
  | write_line add 1 2.0
  |            ^
```

| Kind | When it happens | Examples |
| --- | --- | --- |
| Parse error | Reading the words and working out the program's structure | Unterminated string, a symbol such as `+`, an int literal that is too large, missing `end`, `break` outside a loop, a call with too few values, a leftover value, a `define` without an initial value, a record constructor used as a statement, a record that contains itself, code after a `return`, `break` or `continue`, a minus sign joined to the word before it, code nested more than 1,000 levels deep |
| Type error | Checking types before running | `define` or `set` with the wrong type, a non-`bool` condition, a missing `return` path, an undeclared variable, a built-in's result thrown away |
| Runtime error | While running | Division by zero, invalid `convert` (also a text that names no variant of a choice), `read` past end of input, a missing or unreadable file or directory, a text position out of range, `split` at an empty separator, a list index out of range, a missing map key, an unknown style in a variable, an `exit` code outside 0–255, too deep recursion, a function reading a global whose `define` has not run yet |

- Parse and type errors stop the program before anything runs. All type errors are listed at once.
- **Several parse errors.** A file reports one parse error per function or other declaration, each in the file it is in, because after an error Pyrop goes on at the next `function`, `record` or `choice`. Type errors appear once the parse errors are fixed. Input typed at the `pyrop` prompt stops at its first error.
- **Leftover values.** If a value appears where a statement should start, the operation before it was probably given too many arguments:

  ```
  write_line add 1 2 3
  ```

  This fails with "'3' cannot stand alone as a statement".
- **Too few values.** If a call runs out of values, the error names the call and where it starts, because the line below it is usually fine:

  ```
  write_line add 1
  write_line "next"
  ```

  This fails with "'add' needs 2 values but has only 1: it starts at line 1, column 12, and 'write_line' on line 2 begins a new statement".

## Finding an arity mistake

When an error makes no sense, check how Pyrop grouped the program:

```
pyrop --tree main.prp
```

```
write_line (add 1 2)
set total (multiply price 2)
Parse error main.prp:2:28 '3' cannot stand alone as a statement. …
```

- Every call that is an argument gets brackets, so you see where each call ends.
- Each body goes on its own indented line.
- Nothing is run.
- If there is a parse error, everything before it is still shown, so the last line printed is where the grouping went wrong.
