# 11. Full-screen programs: views and keys

A game's inventory screen or a dashboard fills the whole terminal and changes as keys are pressed. In Pyrop the program never moves the cursor or measures the window. It describes what should be on the screen as a **view**, and Pyrop draws it at whatever size the terminal has. When the output goes to a file or a screen reader, the same view becomes plain lines, as `choose` and `write_table` do in [chapter 10](10-talking-to-the-user.md). The examples here show that plain form.

Views come from the built-in library `terminal`, like the libraries in [chapter 9](09-modules-and-libraries.md).

## A first view

A view is a value of the choice `View`. Importing `View` brings the type, and each kind a program writes directly, such as `panel`, `rows` or `bar`, is named in the same `import`. `write_view` writes a view into the ordinary output, between other lines:

```pyrop
use terminal library
import from terminal View text bar panel rows in_style write_view end

write_line "Before the panel"
write_view panel "Hero" rows list_of View
    text "Ann the brave"
    bar "Health" 7 20
    in_style "danger" text "Poisoned"
end
write_line "After the panel"
```

```output
Before the panel
Hero
Ann the brave
Health: 7 of 20
Poisoned
After the panel
```

On a terminal, the panel has a border with its title in it, the bar is drawn filled to 7 out of 20, and `Poisoned` is in the danger colour. In plain output each part is a line that reads in order.

Views nest, so a whole screen is one view made of smaller ones. These are all the kinds:

| Kind | Takes | Shows |
| --- | --- | --- |
| `text` | a `string` | text, wrapped to the width it gets |
| `table` | headers and rows, as `write_table` | a table |
| `menu` | a `list string` and the selected position | a list with one entry selected |
| `bar` | a label, a value and a maximum | a filled bar, such as health or progress |
| `panel` | a title and a `View` | a view inside a border |
| `rows` | a `list View` | views stacked from top to bottom |
| `columns` | a `list View` | views side by side, sharing the width |
| `grid` | a `list string`, a column and a row | a map, kept around the given position |
| `in_style` | a style and a `View` | a view in one of the styles from chapter 10 |
| `in_color` | a colour and a `View` | a view in a colour, for decoration |
| `colored_grid` | a `list string`, a `map string string` of colours, a column and a row | a map whose characters have colours |

## Showing a screen and reading keys

`show` draws a view as the whole screen. Each `show` replaces the picture, so a program shows its screen, waits for a key with `read_key`, changes its state and shows the screen again. `read_key` gives the key's name: a letter or a digit as typed, or a word such as `up`, `down`, `enter`, `escape` or `space`.

When input is given ahead of time, each line is one key name. That is how this example is run:

```pyrop
use terminal library
import from terminal View menu panel show read_key end

define list string items list_of string "lamp" "rope" "bread" end
define int last subtract length_of items 1
define int cursor 0
define bool done false
while not done block
    show panel "Bag" menu items cursor
    define string key read_key
    if and equal key "down" less cursor last set cursor add cursor 1
    else if and equal key "up" greater cursor 0 set cursor subtract cursor 1
    else if equal key "enter" set done true
end
write_line add "You take the " get items cursor
```

```input
down
down
up
enter
```

```output
Bag
lamp (selected)
rope
bread

Bag
lamp
rope (selected)
bread

Bag
lamp
rope
bread (selected)

Bag
lamp
rope (selected)
bread

You take the rope
```

Each key moves the selection, `up` brings it back to `rope`, and `enter` takes it. In plain output every screen is written in turn and ends with an empty line, so a run with keys given ahead of time is a record of everything the user would have seen. A panel that did not change since the previous screen is left out, so a listener hears only what a key changed.

On a terminal, `show` switches to a screen of its own and hides the cursor. Anything else the program writes, a question it asks and the end of the program all bring the normal screen back, so a program can show a screen and then simply `ask` something.

A menu position that has no entry selects nothing. It is not an error, so an empty inventory needs no special case.

## Pyrop does the layout

The program decides what is on the screen; Pyrop decides where. `columns` shares the width equally, text wraps, a menu scrolls so the selected entry stays visible, and a table shows the rows that fit and says how many more there are. When the window is resized, the screen is laid out again by itself. A window too small for the screen shows `Make the window larger to see this screen.` until it is larger.

That is why there is no way to ask how big the terminal is: a program that measured it would also have to handle every resize.

## Maps

A `grid` draws lines of text exactly as they are, without wrapping, which suits a map. The column and the row after the lines, counted from 0, are the position to keep in view, usually the player. When the map is larger than the screen, Pyrop crops it around that position, so the map scrolls as the player moves:

```pyrop
use terminal library
import from terminal View grid write_view end

write_view grid list_of string
    "#######"
    "#..@..#"
    "#.....#"
    "#######"
end 3 1
```

```output
#######
#..@..#
#.....#
#######
```

## Colours

Styles say what something means, such as `danger` or `success`. A game also colours things that mean nothing in particular: a green goblin, grey walls, blue water. Views can carry colours for that. `in_color` colours a whole view, and `colored_grid` is a `grid` with a map from a character to its colour, so every `g` on the map is green:

```pyrop
use terminal library
import from terminal View text rows in_color colored_grid write_view end

define map string string colors map_of string string
    "#" "gray"
    "~" "blue"
    "g" "green"
    "@" "bright_white"
end
write_view rows list_of View
    colored_grid list_of string
        "#######"
        "#.@~~g#"
        "#######"
    end colors 2 1
    in_color "bright_yellow" text "Gold: 12"
end
```

```output
#######
#.@~~g#
#######
Gold: 12
```

The colours are the 16 every terminal has, shown in the terminal's own palette: `black`, `red`, `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`, `gray`, and `bright_red`, `bright_green`, `bright_yellow`, `bright_blue`, `bright_magenta`, `bright_cyan` and `bright_white`. A character that is not in the map keeps the style or colour around the grid.

Colour is decoration only. In plain output, and for anyone who cannot see it, it is gone, as the output above shows, so never say something through colour alone: the goblin is still a `g`, and a warning still needs the `warning` style or its own words.

## Building a screen from the program's state

A screen is usually built by a function from the state of the program, here a record:

```pyrop
use terminal library
import from terminal View text bar panel rows columns write_view end

record Hero string name int health int gold end

function View screen_of Hero hero
    return columns list_of View
        panel "Hero" rows list_of View
            text the name of hero
            bar "Health" the health of hero 20
        end
        panel "Purse" text fill "{} gold" list_of int the gold of hero end
    end

write_view screen_of Hero "Ann" 12 30
```

```output
Hero
Ann
Health: 12 of 20
Purse
30 gold
```

On a terminal, the two panels stand side by side. A view is an ordinary value, so it can be kept in a variable, passed to a function and compared with `equal`, like any other choice value.

## Waiting with a time limit

A dashboard should change even when nobody presses a key. `read_key_within` waits at most the given number of milliseconds and gives `some` key, or nothing when the time runs out, so the loop shows the screen again about once a second:

```pyrop
use terminal library
import from terminal View text show read_key_within end

define int ticks 0
while less ticks 3 block
    show text fill "Tick {}" list_of int ticks end
    if_some string key read_key_within 1000
        if equal key "q" exit 0
    set ticks add ticks 1
end
```

```input


q
```

```output
Tick 0

Tick 1

Tick 2

```

When keys are given ahead of time, an empty line stands for the time running out, so the first two waits give nothing and the third reads `q`.

## The names an import takes

Importing `View` brings only the type. Each kind a file writes without `from terminal` is named in the same `import`, and only the names a file imports are taken, so a file that imports `rows` cannot also have a variable `rows`:

```pyrop
use terminal library
import from terminal View rows write_view end

define int rows 3
```

```error
'rows' is an imported variant and cannot be used as a variable name.
```

A file that wants one of those names for a variable leaves that kind out of its `import` and writes it qualified, with `from terminal`. A later version of Pyrop can add kinds to `View` without taking any name from a program:

```pyrop
use terminal library
import from terminal View bar write_view end

define int rows 3
write_view bar "Rows" rows 10
write_view from terminal text "done"
```

```output
Rows: 3 of 10
done
```

## Two complete programs

The `samples` folder that comes with Pyrop has two full-screen programs built this way: `samples/rogue/`, a small roguelite in four modules with a map, an inventory and saved progress, and `samples/logwatch/`, a dashboard that follows a log file and redraws once a second. The [reference page for the library `terminal`](../reference/operations/terminal.md) lists every kind, every key name and the rules for fitting the window.
