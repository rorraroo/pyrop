# Dates and waiting: the library `time`

These come from the [library](../statements/libraries.md) `time`: add `use time library`.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `now` | 0 | none | `Moment` | the time on this computer |
| `now_utc` | 0 | none | `Moment` | the same instant in UTC |
| `moment_text` | 2 | `Moment`, `string` layout | `string` | `moment_text today "{year}-{month}-{day}"` → `"2026-11-09"` |
| `parse_moment` | 2 | `string` text, `string` layout | `Moment` | `parse_moment "2026-11-09" "{year}-{month}-{day}"` |
| `milliseconds_between` | 2 | `Moment` earlier, `Moment` later | `int` | `milliseconds_between start finish` → `1500` |
| `add_milliseconds` | 2 | `Moment`, `int` count | `Moment` | `add_milliseconds today 86400000` → one day later |
| `is_before` | 2 | `Moment`, `Moment` | `bool` | `is_before start finish` → `true` |
| `wait` | 1 | `int` milliseconds | none | `wait 500` |

A `Moment` is a record with the `int` fields `year`, `month`, `day`, `hour`, `minute`, `second`, `millisecond` and `offset_minutes`, its distance from UTC. Read a part with `the year of today`.

```pyrop
use time library
import from time Moment moment_text parse_moment add_milliseconds milliseconds_between is_before end

define string layout "{year}-{month}-{day} {hour}:{minute} {offset}"
define Moment start parse_moment "2026-03-29 09:30 +01:00" layout
define Moment later add_milliseconds start 5400000

write_line the hour of later
write_line moment_text later "{weekday} {day} {month_name} at {hour}:{minute}"
write_line milliseconds_between start later
write_line is_before start later
```

```output
11
Sunday 29 March at 11:00
5400000
true
```

- **`now`** is local time, and **`now_utc`** is the same instant with offset 0. Use UTC for data that moves between computers.
- **A duration is an `int` of milliseconds.** One second is 1000, one minute 60000, one hour 3600000.
- **`is_before` and `milliseconds_between` compare instants**, so they are right across a daylight-saving change and between local and UTC. `equal` compares the fields, so the same instant at two offsets is not `equal`.
- **`wait`** pauses the program for 0 to 2147483647 milliseconds.
- **A moment you build by hand is checked when it is used.** Month 13 or 30 February is a runtime error: `This is not a real moment: its month is 13.`

## Layouts

A layout is text with named parts in braces. Write `{{` and `}}` for literal braces.

| Part | Writes | Example |
| --- | --- | --- |
| `{year}` | 4 digits | `2026` |
| `{month}`, `{day}`, `{hour}`, `{minute}`, `{second}` | 2 digits | `09` |
| `{millisecond}` | 3 digits | `250` |
| `{offset}` | distance from UTC | `+01:00` |
| `{month_name}` | the month in English | `November` |
| `{weekday}` | the day in English | `Monday` |

Names are always English, whatever the computer's language.

`parse_moment` reads the same layouts, with these rules:

- `{year}` is required, and no part may appear twice.
- Each number takes up to its width in digits, so both `2026-1-5` and `20260105` can be read with the right layout. A year takes exactly four, so `26-1-5` is not read as the year 26.
- A missing month or day is 1, and any other missing part is 0.
- Without `{offset}`, the moment takes the local offset of that date. `{offset}` also accepts `Z`.
- `{month_name}` and `{weekday}` can only be written.

A layout written as a literal is checked before the program runs, so a misspelt part is reported at once. Text that does not fit is a runtime error, which [`attempt`](../statements/attempt.md) turns into `none`:

```pyrop
use time library
import from time Moment parse_moment end

if_some Moment day attempt parse_moment "2026-02-30" "{year}-{month}-{day}" write_line "A real day."
else write_line "Not a real day."
```

```output
Not a real day.
```
