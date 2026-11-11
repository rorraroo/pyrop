# 9. Programs in several files: modules and built-in libraries

## Modules

When a program grows, split it into files. Every file is a **module**. A file decides what other files may use by marking it with `export`:

```prp shapes.prp
export function float circle_area float radius
    return multiply pi multiply radius radius

define float pi 3.14159
```

Another file loads it with `use`, giving it a name of its own choosing, and calls what it exports with `from <module> <name>`:

```pyrop
use shapes "shapes.prp"

write_line from shapes circle_area 2.0
```

```output
12.56636
```

`from shapes circle_area` acts like one word: it takes as many arguments as `circle_area` has parameters. The path is relative to the file that contains the `use`.

When a file uses something often, `import` lets it leave out `from <module>`. List the names up to `end`:

```pyrop
use shapes "shapes.prp"
import from shapes circle_area end

write_line circle_area 1.0
```

```output
3.14159
```

What a module does not export stays private: `pi` above is a variable of `shapes.prp`, and no other file can read or change it. Records and choices are exported the same way, with `export record` and `export choice`, and importing a choice imports only the type, so each variant a file writes without `from` is named in the `import` too. A module's name is only ever written after `use`, `from` or `import from`, so it does not take the word away: a file using `hero.prp` as `hero` can still have a parameter called `hero`.

## Built-in libraries

Pyrop's own operations come in two groups. The ones nearly every program needs, such as `add`, `length_of` and `ask`, are always there. The rest come in **built-in libraries**, which a program loads with `use … library`:

| Library | Gives |
| --- | --- |
| `math` | `absolute`, `sign`, `power`, `square_root`, `round`, `round_down` |
| `random` | `float_between`, `roll`, `seed` |
| `files` | reading, writing, copying and deleting files, and working with folders and paths |
| `program` | `arguments`, `environment`, `run`, `run_attached`, the record `CommandResult` |
| `terminal` | views and keys, for full-screen programs |
| `time` | the current date and time, writing and reading dates, `wait` |
| `data` | JSON and YAML |

A library is used exactly like a module:

```pyrop
use math library
import from math square_root end

write_line square_root 16.0
write_line from math round 2.5
```

```output
4.0
3
```

The words a library offers are not reserved. A program that does not import `round` may still call a variable `round`, so loading a library never breaks your names.

## Random numbers

`roll` gives a whole number between two bounds, both included. `seed` makes the numbers that follow repeat exactly from run to run, which helps when testing a game:

```pyrop
use random library
import from random roll seed end

seed 42
define int first roll 1 6
seed 42
write_line equal first roll 1 6
```

```output
true
```

## Files

The library `files` reads and writes text files. A program that does not use it cannot touch files at all, so its `use` lines tell you what it may do.

```pyrop
use files library
import from files save load end

save "notes.txt" "Buy milk\n"
write load "notes.txt"
```

```output
Buy milk
```

`save` gives no value, so `attempt save …` gives a `bool` that says whether it worked:

```pyrop
use files library
import from files save end

if not attempt save "notes.txt" "Buy milk\n"
    write_error_line "Could not save the notes."
else write_line "Saved."
```

```output
Saved.
```

A program can keep a whole record in a file and read it back, because `convert` turns a record into text and that text back into the record. Each step that can fail gets its own `attempt`:

```pyrop
use files library
import from files save load end

record Progress int runs int best_score end

define Progress progress Progress 0 0
if_some string text attempt load "progress.txt"
    if_some Progress saved attempt convert Progress text set progress saved
set progress copy progress with runs add the runs of progress 1 end
save "progress.txt" convert string progress
write_line fill "This is run number {}." list_of int the runs of progress end
```

```output
This is run number 1.
```

The file then holds `Progress runs 1 best_score 0`. A missing file and a damaged one both leave the starting value in place.
