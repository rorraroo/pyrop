# Two finished programs

The earlier chapters showed one feature at a time. This one reads two complete programs that come with Pyrop, to show how the features fit together: `samples/logwatch/`, a live dashboard for a log file, and `samples/rogue/`, a small dungeon game. Neither is long. The dashboard is 77 lines and the game about 450, split over four files.

The code shown here is copied from those files, and a test checks that it still matches them.

Run them first:

```
pyrop samples/logwatch/logwatch.prp samples/logwatch/server.log
pyrop samples/rogue/rogue.prp
```

## The dashboard

`logwatch` reads a log every second and shows how many lines of each level it has. It has two files, one that counts and one that draws.

### Counting is separate from drawing

`tally.prp` knows nothing about the screen. It turns lines into a record:

```prp from samples/logwatch/tally.prp
export record Tally int lines int infos int warnings list string errors end
```

That split means the counting can be tested with plain lists of text and no terminal. Each file then has one reason to change.

### A screen is a value

The drawing is a function from a `Tally` to a `View`. It writes nothing. It only describes the screen, and the host decides how to lay that out on the terminal it has:

```prp from samples/logwatch/logwatch.prp
function View levels Tally counted block
    define int total the lines of counted
    return panel "Levels" rows list_of View
        bar "Info" the infos of counted total
        in_style "warning" bar "Warnings" the warnings of counted total
        in_style "danger" bar "Errors" length_of the errors of counted total
    end
end
```

Read the `bar` lines by arity: `bar` takes a label, a value and a maximum, so each line is complete on its own. `in_style` takes a style and a view, and the view it takes is the `bar` that follows.

Because a screen is an ordinary value, one function can build it from smaller ones:

```prp from samples/logwatch/logwatch.prp
function View dashboard Tally counted
    return rows list_of View
        columns list_of View levels counted file_table counted end
        panel "Errors" menu the errors of counted selected
        in_style "muted" text "up/down select   q quit"
    end
```

### The loop

The whole program runs in one loop: read, count, show, wait.

```prp from samples/logwatch/logwatch.prp
while true block
    define Tally counted tally_of value_or attempt load_lines path list_of string end
    set selected moved selected "" length_of the errors of counted
    set refreshes add refreshes 1
    show dashboard counted
    if_some string key read_key_within 1000 block
        if equal key "q" exit 0
        set selected moved selected key length_of the errors of counted
    end
end
```

Two lines do a lot here.

`value_or attempt load_lines path list_of string end` gives the file's lines, or an empty list if it cannot be read. A log that does not exist yet simply shows as empty, and appears when it is created.

`read_key_within 1000` waits up to a second for a key and gives an `option string`. No key means the time ran out, so the `if_some` is skipped and the loop goes round and reads the file again. That one operation is both the keyboard handling and the timer.

### Keeping a selection in range

The list of errors changes while the program runs, so the selected position can end up past the end of it. `moved` handles the key and the limits together:

```pyrop
function int moved int position string key int count block
    if equal key "down" set position add position 1
    else if equal key "up" set position subtract position 1
    return larger_of 0 smaller_of position subtract count 1
end

write_line moved 0 "up" 3
write_line moved 0 "down" 3
write_line moved 2 "down" 3
```

```output
0
1
2
```

The loop calls it with an empty key on every refresh, which changes nothing but the limits. The real function in `logwatch.prp` also handles `home` and `end`.

## The game

`rogue` has four files, and each has one job:

| File | What it holds |
| --- | --- |
| `dungeon.prp` | Generating a level from the seed, and turning it into lines of text |
| `rules.prp` | The hero, the game state, and what each turn does |
| `progress.prp` | What is saved between runs |
| `rogue.prp` | The screens and the main loop |

Only `rogue.prp` knows about the terminal, and it is the only file that imports `View`.

### A turn is a function from a game to a game

Values in Pyrop never change in place, so there is no game object for functions to modify. Instead the state of a run is one record, and only the current screen and cursor live beside it in `rogue.prp`:

```prp from samples/rogue/rules.prp
export record Hero int x int y int health int max_health int attack int gold int depth list Item items end
export record Game Hero hero Level level list string log end
```

Every rule that changes the game takes a `Game` and gives back a new one. The pattern is small enough to try on its own:

```pyrop
record Game int score list string log end

function Game said Game game string message
    return copy game with log push the log of game message end

function Game scored Game game int points
    return said copy game with score add the score of game points end "You scored."

define Game game Game 0 list_of string end
set game scored game 5
set game scored game 2
write_line the score of game
write_line join the log of game "\n"
```

```output
7
You scored.
You scored.
```

In the game this reads as a sentence. Handling a key is a list of cases, each one a function that gives the next game:

```prp from samples/rogue/rules.prp
export function Game take_turn Game game string key
    if equal key "up" return step game 0 -1
    else if equal key "down" return step game 0 1
    else if equal key "left" return step game -1 0
    else if equal key "right" return step game 1 0
    else if equal key "space" return monsters_act said game "You wait."
    else return game
```

The rules keep no state between turns, so any rule can be understood from its parameters alone.

### One `match` to draw, one to act

The game is either exploring or looking at the inventory. That is a choice, and the main loop matches on it twice, once to show the screen and once to handle the key:

```prp from samples/rogue/rogue.prp
while not run_over the hero of game block
    match mode
        when exploring show exploring_screen game
        when inventory show inventory_screen the hero of game cursor
    end
    define string key read_key
    define int pack length_of the items of the hero of game
    match mode
        when exploring
            if equal key "q" break
            else if equal key "i" block
                set mode inventory
                set cursor 0
            end
            else set game take_turn game key
        when inventory
            if equal key "escape" set mode exploring
            else if equal key "enter" block
                set game use_item game cursor
                set cursor moved_cursor cursor "" subtract pack 1
            end
            else set cursor moved_cursor cursor key pack
    end
end
```

Add a third mode and the checker points at both `match`es until each one handles it.

### Saving is one line each way

`convert Data` turns a record into data, `yaml_text` writes that as YAML, and `parse_yaml` and `convert Progress` read it back. Saving needs no format of its own, and the file is one a person can read:

```prp from samples/rogue/progress.prp
export function Progress load_progress string path block
    if_some string text attempt load path
        if_some Data document attempt parse_yaml text
            if_some Progress saved attempt convert Progress document
                if believable saved return saved
    return fresh_progress
end
```

Notice the three `attempt`s. Each one catches only its own operation, so reading the file, understanding it as YAML and finding a `Progress` in it are separate steps. A missing file, a damaged file and implausible numbers all end in the last line, a fresh start.

`believable` is the program's own rule. `convert` proves that the text is a well-formed `Progress`, and only the game can say that minus three escapes makes no sense.

### The last screen stays

A screen from `show` disappears when the program ends, because the terminal goes back to what it showed before. So the final message uses `write_view`, which puts the same kind of view into ordinary output, where it stays:

```prp from samples/rogue/rogue.prp
    write_view panel "The run is over" rows list_of View
```

## What to take from them

- **Keep the terminal in one file.** Rules and data that know nothing about the screen are easy to test and to change.
- **Describe screens, do not draw them.** A function that gives a `View` also works in a pipe, where the same program writes plain text.
- **Pass one state record through functions.** Each rule shows everything it uses.
- **Use a choice for the program's modes.** `match` then keeps every place that depends on the mode complete.
- **Let `attempt` and `value_or` turn a failure into the ordinary path.** Both programs treat a missing file as a normal start, not an error.

To see how any line groups while you read, run `pyrop --tree samples/rogue/rules.prp`, or hover over a word in an editor.
