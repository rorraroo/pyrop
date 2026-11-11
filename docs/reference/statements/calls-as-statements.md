# Calls as statements

A call of a function, or of an operation that does something besides giving a result, can stand on its own as a statement. Its result, if any, is thrown away:

```pyrop
function void greet string name
    write_line add "Hi " name

greet "Ann"
read                  # skips one line of input
write_line read
```

```input
skip me
keep me
```

```output
Hi Ann
keep me
```

- **A `void` function or operation** can only be called this way. It cannot be used as a value.
- **A function's result** may be thrown away, because the function may do something besides giving it.
- **An operation that only gives a result cannot stand alone.** Values never change in place, so `push bag 2` on its own line changes nothing, and the line is an error that says to use the result, as in `set bag push bag 2`. The same error catches a call that was given too many values, such as the second `add` in `set x add x 1 add x 2`.
- **The operations whose result may be thrown away** are those that do something else as well: `read`, `ask`, `confirm` and `choose`, `run` and `run_attached` from `program`, `roll` and `float_between` from `random`, and `read_key` and `read_key_within` from `terminal`.

```pyrop
define list int bag list_of int end
push bag 2
```

```error
'push' only gives a result, and here the result is thrown away, so this line does nothing. Use the result, as in set <name> push …, or write_line push … to show it. If this line was meant as a value of the call before it, that call was given too many values.
```
