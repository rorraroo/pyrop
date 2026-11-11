# `set`

Assigns a value to a declared variable.

```
set <name> <expression>
```

```pyrop
define int age 0
set age 25
set age add age 1
write_line age
```

```output
26
```

The value's type must match the variable's declared type.
