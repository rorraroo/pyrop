# Pyrop

Language support for **Pyrop**, a small statically typed language written in prefix notation, with no brackets and no symbols.

```
function int factorial int n
    if less n 2 return 1
    else return multiply n factorial subtract n 1

def int i 1
while less i 6 block
    write_line factorial i
    set i add i 1
end
```

## Features

- **Errors while you type.** Syntax, parse and type errors are underlined as you write, in the file they are in, also when that is a module you have not saved yet. They are listed in the **Problems** panel too. Nothing is run.
- **Go to Definition and completion.** Jump from any name to where it is declared, in any file of the program. Completion lists what fits at the cursor and shows how many operands each call takes, such as `takes 2`.
- **Grouping and types on hover.** Hover over a word to see how the call it starts groups, with brackets around each operand, the type it gives, and what it calls: every form of a built-in operation, a function's header, a record's fields or a variant's data. Hover over a variable, parameter or field to see its type.
- **Format Document** indents every line by how the program parses, four spaces per level, and changes nothing else: your line breaks, blank lines and comments stay. A file that does not parse is left as it is. It works with format on save too.
- **Highlighting that knows your program.** Built-in operations, your own functions, variables, parameters, record fields, types, variants and module names each get their own colour, and names imported from another module are shown in italics.
- **Syntax highlighting** for `.prp` files: keywords, types, built-in operations, numbers, strings with escape sequences, and comments.
- **Declarations stand out:** function names and parameters in `function` headers, record names and fields in `record` declarations, choice names and variants in `choice` declarations, new variables in `def`, loop variables in `for`, and unwrapped values in `if_some`.
- **Record and choice types stand out:** the language server colours every record and choice name as a type, whatever its spelling; without it, capitalised words such as `Point` or `Direction` are coloured as types by convention. Field names in `the <field> of` and `copy … with <field>` are coloured as fields.
- **Invalid escape sequences** such as `\q` are marked as errors.
- **Editing helpers:**
  - `Ctrl+/` toggles `#` comments.
  - Quotes close automatically.
  - `block` … `end` regions can be folded.
  - The line after `block` is indented automatically.
- **File icon:** the Pyrop crystal appears next to `.prp` files in the explorer and on editor tabs.

## Requirements

Errors and the program-aware highlighting come from the `pyrop` command-line tool, version 0.11.0 or later (0.20.1 or later for hover and formatting, 0.22.0 or later for Go to Definition and completion), which the extension starts in the background with `pyrop --lsp`. The Windows installer puts `pyrop` on your `Path`; otherwise set **pyrop.path** to the executable. Without it, the extension still highlights `.prp` files from its grammar.

| Setting | Default | Meaning |
| --- | --- | --- |
| `pyrop.path` | `pyrop` | The `pyrop` executable to start |
| `pyrop.languageServer.enabled` | `true` | Turn errors and program-aware highlighting on or off |

The **Pyrop: Restart Language Server** command restarts it, for example after installing a new `pyrop`.

## Running Pyrop programs

To run programs, use the `pyrop` command-line tool:

```
pyrop program.prp     # run a script
pyrop                 # start the interactive REPL
pyrop --help          # show all options
```

## Known limitations

- Without `pyrop`, calls to your own functions are coloured like variables, and any capitalised word is coloured as a type: telling them apart needs the full program.
- A file shows at most one parse error per function or other declaration, because the rest of a broken declaration cannot be read reliably. Type errors are all shown once the parse errors are fixed.
- Untitled files are checked once they are saved.
- Italics for imported names come from a default the extension sets in `editor.semanticTokenColorCustomizations`; if you set that setting yourself, add a rule for `*.imported:pyrop` to keep them.
- The file icon shows with icon themes that have no icon of their own for `.prp`, including the default theme. The **Minimal** and **None** themes show no file icons at all.
