# `if_some` and `else`

Run a statement with the value an option holds, or another statement when it holds none.

```
if_some <type> <name> <option> <statement>
if_some <type> <name> <option> <statement> else <statement>
```

```pyrop
if_some int age attempt convert int read write_line add age 1
else write_line "That is not a number."
```

```input
41
```

```output
42
```

- The option must have the type `option <type>`, with exactly the type you write.
- **The name exists only in the first statement.** It cannot be read in the `else` branch or after the `if_some`.
- The name follows the same rules as a `for` loop variable: it cannot reuse a `define` made earlier in the same block, or a parameter in a function's outermost block.
- An `else` belongs to the nearest `if` or `if_some` without an `else`, so `else if` and `else if_some` chains work.
- See [Optional values](options.md) and [`attempt`](attempt.md).
