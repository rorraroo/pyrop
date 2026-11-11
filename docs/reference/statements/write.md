# `write` and `write_line`

Print a value. `write` adds nothing after the value; `write_line` adds a line break.

```
write <expression>
write_line <expression>
```

```pyrop
write "Name: "
write_line "Pyrop"
write_line add 1 2
```

```output
Name: Pyrop
3
```

Any type can be printed. Floats always print with a decimal point or exponent (`3.0`, `0.1`, `1E+20`), so they are never confused with ints. Booleans print as `true` and `false`.
