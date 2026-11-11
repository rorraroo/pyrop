# Views and keys: the library `terminal`

A screen is described as a **view**, a value of the choice `View`, and Pyrop draws it. Add `use terminal library`; importing `View` brings only the type, and each kind is imported by name, as in `import from terminal View panel bar end`, which leaves the other words free. A kind that is not imported is written `from terminal <kind>`.

```pyrop
use terminal library
import from terminal View text menu bar panel rows columns in_style show read_key end

define int cursor 0
define View hero panel "Hero" rows list_of View
    bar "Health" 7 20
    in_style "danger" text "Poisoned"
end
show columns list_of View hero panel "Bag" menu list_of string "lamp" "rope" end cursor end
if equal read_key "down" set cursor add cursor 1
write_line fill "The cursor is at {}." list_of int cursor end
```

```input
down
```

```output
Hero
Health: 7 of 20
Poisoned
Bag
lamp (selected)
rope

The cursor is at 1.
```

| Kind | Takes | Plain form |
| --- | --- | --- |
| `text` | `string` | the text |
| `table` | `list string` headers, `list list string` rows | as `write_table` |
| `menu` | `list string` entries, `int` selected position | one entry per line, the selected one followed by ` (selected)` |
| `bar` | `string` label, `int` value, `int` maximum | `Health: 7 of 20` |
| `panel` | `string` title, `View` | the title, then the view |
| `rows`, `columns` | `list View` | the views one after another |
| `grid` | `list string` lines, `int` column, `int` row | the lines |
| `in_style` | `string` style, `View` | the view, in that style |
| `in_color` | `string` colour, `View` | the view, in that colour |
| `colored_grid` | `list string` lines, `map string string` colours, `int` column, `int` row | the lines, without colour |

| Operation | Arity | Arguments | Result |
| --- | --- | --- | --- |
| `show` | 1 | `View` | none |
| `write_view` | 1 | `View` | none |
| `read_key` | 0 | none | `string` |
| `read_key_within` | 1 | `int` milliseconds | `option string` |

- **`show`** shows a view as the whole screen; **`write_view`** writes one into the ordinary output. On a terminal, `show` takes over the whole window (its alternate screen, with the cursor hidden) and redraws only what changed; any other output, a prompt or the end of the program returns to the normal screen, and so do an error, `exit` and `Ctrl+C`. Pyrop lays the view out to fit: columns share the width, text wraps, a menu scrolls to its selected entry, a table shows the rows that fit, and a grid is cropped around its focus. When the window is resized, the screen is laid out again while the program waits for a key. A window narrower than 20 columns or lower than 3 lines shows `Make the window larger to see this screen.` When output is plain (redirected, `NO_COLOR` or `--plain`), both write the plain form, and `show` adds an empty line after it, so a run is a transcript of every screen. A plain `show` leaves out each outermost `panel` whose plain form is the same as on the previous screen, when nothing else was written in between and the screen has the same panels with the same titles; everything outside panels is written every time. `write_view` always writes everything.
- A menu position with no entry selects nothing, and a bar shows its numbers as given, so an empty inventory or a count of zero needs no special case.
- **Bars that follow each other in one `rows`** start their bars in the same column on a terminal; the plain form is not padded.
- **Colours** are decoration, dropped in plain output like styles, so nothing may be said through colour alone. `in_color` colours a view and `colored_grid` colours a map's characters through a map from one character to a colour; other characters keep the style or colour around them. The colours are `black`, `red`, `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`, `gray`, `bright_red`, `bright_green`, `bright_yellow`, `bright_blue`, `bright_magenta`, `bright_cyan` and `bright_white`. A colour and a style share one place, so the innermost wins.
- **Checked when shown:** a table without headers or with a row of the wrong size, an unknown style or colour held in a variable, a colour map key that is not one character or is a control character, and a view nested more than 1,000 levels deep are runtime errors, and nothing is written. Under [`attempt`](../statements/attempt.md) they give `false`. An unknown style or colour written as a literal, and a literal colour map key that is not one character, are found before the program runs.
- Views are ordinary values: `equal` compares them, `match` takes them apart, and `write_line` writes how one was made, such as `panel "Hero" (text "ok")`.
- **`read_key`** returns the name of the next key. A printable key is its character (`a`, `A`, `?`); other keys are words: `up`, `down`, `left`, `right`, `enter`, `escape`, `space`, `tab`, `backspace`, `delete`, `insert`, `home`, `end`, `page_up`, `page_down`, `f1` to `f12`, and `control_s` for `Ctrl+S`. Other combinations are ignored. When input is piped, each input line is one key name (`down`, `enter`, `q`), so tests and scripts give keys as lines. At the end of input it is a runtime error, or `none` under `attempt`.
- **`read_key_within <ms>`** waits at most that long: `some` key, or `none` when the time runs out. An empty input line counts as the time running out. At the end of input it waits and gives `none`. A time below 0 or above 2147483647 is a runtime error.

## A report among other output

`write_view` writes a view once, between ordinary lines, without taking over the screen:

```pyrop
use terminal library
import from terminal View text table bar rows in_style write_view end

write_line "Build finished."
write_view rows list_of View
    bar "Tests passed" 41 42
    table list_of string "Step" "Time" end list_of list string
        list_of string "compile" "2.1 s" end
        list_of string "test" "8.4 s" end
    end
    in_style "warning" text "1 test was skipped."
end
```

```output
Build finished.
Tests passed: 41 of 42
Step     Time
compile  2.1 s
test     8.4 s
1 test was skipped.
```

## A screen that updates by itself

`read_key_within` lets a screen change once a second while it waits for `q`. Here the input gives two empty lines, each standing for a second without a key, and then `q`:

```pyrop
use terminal library
import from terminal View text panel show read_key_within end

define int seconds 0
define bool running true
while running block
    show panel "Uptime" text fill "Running for {} s" list_of int seconds end
    if_some string key read_key_within 1000 block
        if equal key "q" set running false
    end
    else set seconds add seconds 1
end
write_line "Stopped."
```

```input


q
```

```output
Uptime
Running for 0 s

Uptime
Running for 1 s

Uptime
Running for 2 s

Stopped.
```
