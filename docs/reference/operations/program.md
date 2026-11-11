# Arguments, environment and commands: the library `program`

These come from the [library](../statements/libraries.md) `program`: add `use program library`.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `arguments` | 0 | none | `list string` | `pyrop report.prp a b` → `["a", "b"]` |
| `environment` | 1 | `string` | `option string` | `environment "HOME"` → `some "/home/ann"` |
| `run` | 2 | `string`, `list string` | `CommandResult` | `run "git" list_of string "status" end` |
| `run_attached` | 2 | `string`, `list string` | `int` | `run_attached "vim" list_of string "notes.txt" end` |

- **`arguments`** gives the command-line arguments after the script's file name, numbered from 0 like any list: `length_of from program arguments` counts them and `get from program arguments 0` is the first. Asking for an index outside the list is `get`'s runtime error.
- In the REPL there are no arguments.
- **`environment`** returns the value of an environment variable as an [option](../statements/options.md): `some` with the text when it is set, even to `""`, and `none` when it is not. An empty name, or one containing `=` or a control character, is a runtime error.

Started as `pyrop tool.prp`, with no arguments and no `EDITOR` set, this program writes:

```pyrop
use program library
import from program arguments environment end

define int count length_of arguments
write_line fill "This program was given {} arguments." list_of int count end
for string argument in arguments write_line argument
if_some string editor environment "EDITOR" write_line add "Your editor is " editor
else write_line "No editor is set."
```

```output
This program was given 0 arguments.
No editor is set.
```

## Running other programs

`run` starts a program, waits for it and gives a `CommandResult`, a record of this library with three fields:

| Field | Type | Holds |
| --- | --- | --- |
| `exit_code` | `int` | The program's exit code |
| `output` | `string` | What it wrote to standard output |
| `errors` | `string` | What it wrote to standard error |

```
use program library
import from program CommandResult run end

define CommandResult result run "git" list_of string "log" "--oneline" end
write_line the output of result
```

- **The program name and its arguments are separate.** No shell is involved, so spaces and quotes in an argument need no escaping, and text from a user cannot turn into another command. To use a shell, run one: `run "sh" list_of string "-c" "ls | wc -l" end`.
- **The program name is only a name.** One with a quotation mark, or a space at its start or end, is an error; arguments go in the list.
- **On Windows** a `.bat` or `.cmd` file such as `npm` cannot be started directly, because Windows would read its arguments as shell commands. Go through `cmd` instead: `run "cmd" list_of string "/c" "npm" "test" end`. Text from a user does not belong in that list, since `cmd` reads it as a command line.
- **A non-zero exit code is a normal result.** Check `exit_code` yourself.
- **A program that cannot be started is a runtime error**, and so is an empty program name. Under [`attempt`](../statements/attempt.md) it gives `none`.
- **Output is read as UTF-8**, and Windows line ends become `\n`, so `split` works the same everywhere.
- **The program's input is empty**, so one that asks a question gets no answer and ends.
- **Output has a limit** of 100,000,000 characters on each stream. A program that writes more is stopped, which is a runtime error, or `none` under `attempt`.

`run_attached` gives the program the terminal itself and returns its exit code. Use it for anything interactive or long-running. Its output is not captured. A screen from `show` is left first, and the next `show` draws it again. When your program's own input is piped, lines Pyrop has already read ahead are not passed on to the command.

This program asks for something that is not installed:

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
