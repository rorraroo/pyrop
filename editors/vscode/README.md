# Pyrop

Language support for **Pyrop**, a small statically typed language written in prefix notation, with no brackets and no symbols.

```
function int factorial int n
    if less n 2 return 1
    else return multiply n factorial subtract n 1

def int i 1
while less i 6 block
    writeline factorial i
    set i add i 1
end
```

## Features

- **Syntax highlighting** for `.prp` files: keywords, types, built-in operations, numbers, strings with escape sequences, and comments.
- **Declarations stand out:** function names and parameters in `function` headers, and new variables in `def`.
- **Invalid escape sequences** such as `\q` are marked as errors.
- **Editing helpers:**
  - `Ctrl+/` toggles `#` comments.
  - Quotes close automatically.
  - `block` … `end` regions can be folded.
  - The line after `block` is indented automatically.
- **File icon:** the Pyrop crystal appears next to `.prp` files in the explorer and on editor tabs.

## Running Pyrop programs

This extension adds editor support only. To run programs, install the `pyrop` command-line tool, then:

```
pyrop program.prp     # run a script
pyrop                 # start the interactive REPL
pyrop --help          # show all options
```

## Known limitations

- Calls to your own functions are coloured like variables. In Pyrop, `square x` looks the same whether `square` is a function or a variable, and telling them apart needs the full program.
- Errors are not shown in the editor yet. They are reported when the program is run.
- The file icon shows with icon themes that have no icon of their own for `.prp`, including the default theme. The **Minimal** and **None** themes show no file icons at all.
