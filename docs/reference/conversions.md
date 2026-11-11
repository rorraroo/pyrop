# Conversions

## `convert`

Converts a value to another type.

```
convert <type> <expression>
```

```pyrop
define int count 7
define float ratio 0.0
write_line add "Total: " convert string 42
set ratio divide convert float count 3.0
```

```output
Total: 42
```

| From ↓ / To → | `int` | `float` | `string` | `bool` |
| --- | --- | --- | --- | --- |
| `int` | unchanged | nearest float; exact up to ±2^53 | `"42"` | not allowed |
| `float` | drops the fraction, toward zero | unchanged | `"3.5"`, `"3.0"` | not allowed |
| `string` | parses `"42"`, `" -7 "` | parses `"3.5"`, `"1e3"` | unchanged | `"true"` / `"false"` only |
| `bool` | not allowed | not allowed | `"true"` / `"false"` | unchanged |

- **"Not allowed"** conversions are type errors, reported before the program runs.
- **Runtime errors:** converting text that is not a valid value, such as `convert int "abc"`, and converting a float that is too large for an int. `attempt convert int "abc"` gives `none` instead.
- **Spaces** around text are ignored when converting from a string.
- **Numbers** always use `.` as the decimal separator, whatever the system language.
- **Lists, maps and options** convert only to `string`, giving the same text as printing them: `convert string list_of int 1 2 end` → `"[1, 2]"`.
- **Records** convert to `string` and back. `convert Player text` reads exactly what `convert string` wrote for a `Player`, so a program saves with `save "game.save" convert string player` and loads with `if_some string text attempt load "game.save"` followed by `if_some Player saved attempt convert Player text …`. Two `attempt`s are needed, one for the missing file and one for a damaged text, because `attempt` catches only its own operation's failure, never one in its arguments. Only surrounding whitespace may differ. Any other text is a runtime error that says where reading stopped, counted in characters from 0, and what was expected there: `Cannot read a Player at position 19: expected the field 'moves'.` Under `attempt` it gives `none`. A save written before the record's fields changed no longer reads, so keep a version line of your own if old saves matter.
- **Choices** convert to `string`, giving the variant's name, and a `string` converts to a choice by naming one of its variants: `convert Direction "north"`. A text that names no variant is a runtime error. A variant that carries data is read with its data, as its text form writes it: `convert Action "go north"`.
