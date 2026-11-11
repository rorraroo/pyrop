# `if` and `else`

Run a statement only when a condition is `true`.

```
if <condition> <statement>
if <condition> <statement> else <statement>
```

```pyrop
define int age 30
if greater age 18 write_line "Adult"
else write_line "Minor"
```

```output
Adult
```

- The condition must be a `bool`.
- Each branch is a **single statement**. Use [`block … end`](block.md) for more than one.
- An `else` belongs to the nearest `if` without an `else`.
- `else if` chains need no special syntax:

```pyrop
define int x -4
if greater x 0 write_line "positive"
else if less x 0 write_line "negative"
else write_line "zero"
```

```output
negative
```
