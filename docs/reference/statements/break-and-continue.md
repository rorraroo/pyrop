# `break` and `continue`

Control the innermost `while` or `for` loop.

```
break
continue
```

- `break` leaves the loop immediately.
- Nothing can follow a `break` or `continue` in the same `block`, because it could never run; that is an error before the program starts.
- `continue` skips the rest of the current iteration. A `while` checks its condition again; a `for` goes on with the next item.

```pyrop
while true block
    define string line read
    if equal line "" continue
    if equal line "quit" break
    write_line line
end
```

```input
one

two
quit
three
```

```output
one
two
```

Both are only allowed inside a `while` or `for` loop in the same function. To leave several nested loops at once, set a flag variable that the outer loop checks.
