# Running Pyrop

| Command | What it does | Exit code |
| --- | --- | --- |
| `pyrop file.prp [arguments]` | Runs a script file, passing it any further arguments. | the code given to [`exit`](statements/exit.md), `0` when the script finishes, `1` on any error |
| `pyrop` | Starts the interactive REPL. | the code given to `exit`, or `0` when the REPL is closed |
| `pyrop --help`, `pyrop -h` | Shows usage and a description of each argument. | `0` |
| `pyrop --version`, `pyrop -v` | Shows the installed version, for example `pyrop 1.0.0`. | `0` |
| `pyrop --build file.prp [-o output]`, `pyrop -b …` | Turns a script into a standalone program. See [Building an executable](#building-an-executable). | `0` when built, `1` on any error, `2` for wrong arguments |
| `pyrop --tree file.prp`, `pyrop -t …` | Shows how the script is grouped, with brackets around every call, without running it. See [Finding an arity mistake](errors.md#finding-an-arity-mistake). | `0` without errors, `1` with errors |
| `pyrop --format file.prp …`, `pyrop -f …` | Gives every line the indentation its parse calls for, four spaces per level, and rewrites the file. Line breaks, blank lines, comments and spacing inside lines stay as you wrote them. A file that does not parse is left unchanged and its error shown. | `0`, or `1` when a file does not parse or cannot be read or written |
| `pyrop --lsp` | Runs the language server that editors start to show errors while you type, to colour functions, variables and imports apart, to show on hover how a call groups and what type it gives, to jump to where a name is declared, to complete names and show how many operands each takes, and to format a document. The VS Code extension starts it for you. | `0` when the editor shuts it down |
| `pyrop --plain …` | Any of the above without colours, styles or prompt decorations. `--plain` must come first. | as above |
| anything else | Shows a short usage message. | `2` |

From the source folder, use `dotnet run --` in place of `pyrop`, for example `dotnet run -- file.prp`.

## Running a script

```
pyrop hello.prp
```

The whole file is checked before anything runs. If it contains any syntax or type errors, all of them are listed and nothing is executed. Input for [`read`](operations/read.md) comes from standard input, so it can be redirected:

```
pyrop ask.prp < answers.txt
```

Words after the file name are passed to the script as [arguments](operations/program.md), even ones starting with `-`:

```
pyrop report.prp sales.txt --short
```

On Linux and macOS, a script whose first line is `#!/usr/bin/env pyrop` can be run directly once it is executable. Pyrop reads that line as a comment.

**Errors go to standard error**, separately from what the program writes, so redirecting output keeps them out of the file and still shows them:

```
pyrop report.prp sales.txt > report.txt
```

A program's own diagnostics, written with [`write_error_line`](statements/write-error.md), go to standard error too.

**Plain mode** writes no colours or styles. It turns on by itself when output is redirected or the `NO_COLOR` environment variable is set, and can be asked for with `pyrop --plain`. Use it with a screen reader or whenever styled output gets in the way.

## Building an executable

```
pyrop --build filter.prp
pyrop --build filter.prp -o tools/filter
```

This creates `filter.exe` (just `filter` on Linux and macOS), a single file that runs on computers without Pyrop or .NET. It contains the script and every module it uses. `-o`, or `--output`, writes it somewhere else under another name. Everything typed after the program's name goes to the program, even `--help`:

```
filter.exe sales.txt --short
```

- The script is checked first. If it has any errors, nothing is built.
- Modules are found inside the program, so it can be copied anywhere. `use` paths must be relative, such as `use text "lib/text.prp"`; an absolute path cannot be built.
- Building needs the **standalone pyrop**, made by running `dotnet publish` in the Pyrop repository. The `pyrop` installed with `dotnet tool` can run scripts but not build them.
- A program is built for the kind of computer it is built on: building on Windows gives a Windows program.

## Using the REPL

```
pyrop> define int x 5
pyrop> write_line multiply x 2
10
```

- Variables and functions you define, and modules you `use` and `import`, stay available for the rest of the session. A module file is loaded once per session; to see changes to it, start a new session.
- **Continuation lines.** If your input is unfinished, such as a `block` or `list_of` without `end`, a `for` without its body, or `add 1` with a missing argument, the prompt changes to `...>` and you can keep typing. Blank lines are fine inside unfinished input. Two empty lines in a row drop it and bring back the normal prompt.
- **Pending `else`.** After an `if` or `if_some` without an `else`, the REPL also waits, because an `else` may follow on the next line. Press Enter on an empty line to run what you have typed.
- **Errors.** If an input has a syntax or type error, nothing in it runs and the session is unchanged. If a runtime error occurs, statements that ran before it keep their effects.
- **Redeclaring.** You can `define` an existing global again with the same type and a new initial value, which may use the old one: `define int x add x 1`. Changing its type is an error.
- **Exiting.** Type `exit 0`, or press Ctrl+Z then Enter on Windows, or Ctrl+D on Linux and macOS.
