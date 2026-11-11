# 10. Talking to the user: prompts, menus and tables

Pyrop describes what a program wants to say or ask, and lets the terminal decide how to show it. On an interactive terminal a menu can be picked with the arrow keys; when the output goes to a file or a screen reader, the same menu becomes numbered lines. The examples here show that plain form, which is also what you get when input is given ahead of time, as shown in the input boxes.

## Asking

`ask` writes a question and gives back the line the user types. `confirm` asks a yes-or-no question and gives a `bool`:

```pyrop
define string name ask "What is your name? "
if confirm "Shall I shout?" write_line uppercase name
else write_line name
```

```input
Ann
y
```

```output
What is your name? 
Shall I shout? [y/n] 
ANN
```

The answers the user types do not appear in the output above, but each question still ends its line, as it would on a terminal after Enter.

## Menus

`choose` shows a question and a list of entries, and gives back the entry the user picks:

```pyrop
define string drink choose "What would you like?" list_of string "Tea" "Coffee" "Water" end
write_line add "Here is your " lowercase drink
```

```input
2
```

```output
What would you like?
1. Tea
2. Coffee
3. Water
Choose a number from 1 to 3: 
Here is your coffee
```

## Tables

`write_table` takes the headers and the rows, each row a list of texts, and lines the columns up:

```pyrop
write_table list_of string "Item" "Price" end list_of list string
    list_of string "Lamp" "5" end
    list_of string "Rope" "12" end
end
```

```output
Item  Price
Lamp  5
Rope  12
```

## Styles

`styled` gives everything a statement writes a style, named by what it means: `emphasis`, `muted`, `success`, `warning`, `danger` or `info`. A terminal shows the style in colour, and plain output simply leaves it out:

```pyrop
styled "warning" write_line "The disk is almost full."
```

```output
The disk is almost full.
```

Styles carry meaning. Colours by name, for decorating a game's screen, belong to views and come in [chapter 11](11-views-and-keys.md#colours).

## Reading everything that comes in

`read` reads one line. `input_ended` tells whether there is nothing left to read, so a program can process all of its input, line by line:

```pyrop
define int count 0
while not input_ended block
    define string line read
    if not equal trim line "" set count add count 1
end
write_line fill "{} lines with text" list_of int count end
```

```input
first

third
```

```output
2 lines with text
```

## Errors and exit codes

`write_error_line` writes to the error output instead of the normal output, so error messages still reach the user when the normal output goes into a file. `exit` ends the program with a code that tells the shell whether it worked, 0 for success:

```pyrop
define string answer ask "Pick a number: "
if_some int number attempt convert int answer
    write_line multiply number 2
else block
    write_error_line "That is not a number."
    exit 2
end
```

```input
21
```

```output
Pick a number: 
42
```

## Arguments

A program started as `pyrop report.prp data.txt` gets `data.txt` from the library `program`. Run here, it is given no arguments, so it shows its usage message:

```pyrop
use program library
import from program arguments end

if equal length_of arguments 0 block
    write_error_line "Usage: report <file>"
    exit 2
end
write_line add "Reading " get arguments 0
```

```output
Usage: report <file>
```

Next: [Full-screen programs: views and keys](11-views-and-keys.md)

## Running other programs

`run` from the library `program` starts another program and gives its exit code, output and errors. The program's name and each argument are separate values, so nothing needs quoting:

```
use program library
import from program CommandResult run end

define CommandResult result run "git" list_of string "log" "--oneline" end
if equal the exit_code of result 0 write_line the output of result
else write_error_line the errors of result
```

A non-zero exit code is an ordinary result that the program can look at. Only a program that cannot be started at all is an error, so `attempt` can handle it:

```pyrop
use program library
import from program CommandResult run end

if_some CommandResult result attempt run "a_program_that_is_not_installed" list_of string end
    write_line the output of result
else write_line "That program could not be started."
```

```output
That program could not be started.
```

For a program that needs the terminal itself, such as an editor, use `run_attached`, which returns the exit code. The [reference](../reference/operations/program.md) has the details, and `samples/gitsummary/` is a complete example.
