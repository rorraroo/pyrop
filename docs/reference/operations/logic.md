# Logic

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `and` | 2 | `bool bool` | `bool` | `and true false` → `false` |
| `or` | 2 | `bool bool` | `bool` | `or true false` → `true` |
| `not` | 1 | `bool` | `bool` | `not true` → `false` |

`and` and `or` skip their second argument when the first already decides the result:

```pyrop
define int n 10
define int d 0
write_line and greater d 0 greater divide n d 1    # safe even when d is 0
```

```output
false
```
