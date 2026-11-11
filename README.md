# Pyrop

Pyrop is a programming language written in plain words: no brackets and no operator symbols, so every program can be read aloud, dictated and followed with a screen reader. It is statically typed, and works for anything from a ten-line utility to a full game in the terminal.

```pyrop
define string name ask "What is your name?"
define int letters length_of name
write_line fill "Hello, {}! Your name has {} letters." list_of string name convert string letters end
```

Each operation takes a fixed number of values, so Pyrop knows how words group without brackets: `multiply add 1 2 subtract 10 4` is (1 + 2) × (10 − 4).

## What it offers

- **Types checked before the program runs,** with records, choices with data, lists, maps and optional values, all of them values that never change in place.
- **Modules,** with `use`, `import` and `export`, and built-in libraries for files, JSON and YAML, time, maths, random numbers, running other programs and the terminal.
- **Console interfaces** described as views (panels, menus, tables, bars and coloured maps) that draw full-screen on a terminal and turn into plain text in a pipe or a screen reader.
- **Standalone programs:** `pyrop --build` turns a script into one executable that runs without Pyrop or .NET.
- **Editor support:** a VS Code extension with highlighting, errors as you type, hover, go to definition, completion and formatting.

## Getting started

On Windows, run the installer `pyrop-setup-<version>.exe`. It puts `pyrop` on your `Path`, opens `.prp` files with it and can install the VS Code extension.

From source, with the .NET 10 SDK:

```sh
dotnet publish                  # a standalone pyrop for this system, in publish/
dotnet test                     # run the tests
```

Then:

```sh
pyrop hello.prp                 # run a script
pyrop                           # start the REPL
pyrop --build hello.prp         # build a standalone program
pyrop --help                    # everything else
```

## Learning Pyrop

- The [guide](docs/guide/index.md) teaches the language from the start.
- The [reference](docs/reference/index.md) describes every statement and operation.
- `dotnet run --project tools/DocsSite` builds both into a website in `publish/site/`.
- The [samples](samples/) are complete programs: a log checker, a text adventure, a roguelite, a live log dashboard, a git summary and a backup tool.

## License

Pyrop is released under the [MIT License](LICENSE).
