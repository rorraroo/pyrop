# Random numbers: the library `random`

These come from the [library](../statements/libraries.md) `random`: add `use random library`. Qualified, they read as what they do: `from random roll 1 6`.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `roll` | 2 | `int` lower, `int` upper | `int` | `roll 1 6` → a number from 1 to 6 |
| `float_between` | 2 | `float` lower, `float` upper | `float` | `float_between 0.0 1.0` → at least 0.0, below 1.0 |
| `seed` | 1 | `int` | none | `seed 42` |

This program is seeded, so it gives the same rolls every time it runs, on every computer. Leave out `seed 42` and they change from run to run:

```pyrop
use random library
import from random roll float_between seed end

seed 42
define list int rolls list_of int roll 1 6 roll 1 6 roll 1 6 end
write_line rolls
define float chance float_between 0.0 1.0
if less chance 0.5 write_line "The door is locked."
else write_line "The door opens."
```

```output
[1, 1, 6]
The door opens.
```

- `roll` includes both bounds. `float_between` includes the lower bound but never reaches the upper one.
- Bounds in the wrong order are a runtime error.
- **`seed`** makes every following `roll` and `float_between` repeat the same sequence each run, on every computer, which helps with testing and shared game seeds. It can only be used as a statement.

```pyrop
use random library

write_line from random roll 6 1
```

```error
'roll' needs a lower bound no greater than its upper bound, found 6 and 1.
```
