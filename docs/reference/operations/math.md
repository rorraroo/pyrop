# Math: the library `math`

These come from the [library](../statements/libraries.md) `math`: add `use math library`, then import them or write `from math round 2.5`. `smaller_of` and `larger_of` are in the core, with the comparisons.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `absolute` | 1 | `int`, `float` | same type | `absolute -5` → `5` |
| `sign` | 1 | `int`, `float` | same type | `sign -5` → `-1`, `sign 0.0` → `0.0` |
| `power` | 2 | `int int`, `float float` | same type | `power 2 10` → `1024` |
| `square_root` | 1 | `float` | `float` | `square_root 9.0` → `3.0` |
| `round` | 1 | `float` | `int` | `round 2.5` → `3`, `round -2.5` → `-3` |
| `round_down` | 1 | `float` | `int` | `round_down 2.7` → `2`, `round_down -2.5` → `-3` |

```pyrop
use math library
import from math absolute sign power square_root round round_down end

define float price 19.99
write_line round price
write_line round_down price
write_line absolute subtract 3 10
write_line sign subtract 3 10
write_line power 2 10
write_line fill "The diagonal of a 3 by 4 box is {}." list_of float square_root add power 3.0 2.0 power 4.0 2.0 end
```

```output
20
19
7
-1
1024
The diagonal of a 3 by 4 box is 5.0.
```

- `round` rounds halves away from zero. `round_down` always rounds toward the smaller number.
- Both give an `int`, ready to use as a count. A float too large for an int is a runtime error.
- `sign` is `1`, `-1` or `0`, of the number's own type, so a step towards a target is `sign subtract target x`.
- `power` on ints cannot take a negative exponent; use floats: `power 2.0 -1.0` → `0.5`.
- `square_root` of a negative number, and float results that are not real numbers, such as `power -8.0 0.5`, are runtime errors.

A failure stops the program with a message that names the value:

```pyrop
use math library

define float distance from math square_root -4.0
```

```error
Cannot take the square root of a negative number (-4.0).
```
