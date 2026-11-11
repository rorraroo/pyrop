# Pyrop

Language support for Pyrop, a programming language written in plain words: no brackets and no operator symbols, so every program can be read aloud, dictated and followed with a screen reader. The extension checks your program as you type and shows how its words group.

```pyrop
function int factorial int n
    if less n 2 return 1
    else return multiply n factorial subtract n 1

define int i 1
while less i 6 block
    write_line factorial i
    set i add i 1
end
```

Each operation takes a fixed number of values, so Pyrop knows how words group without brackets. Hover over `multiply` above and the extension shows it as `multiply n (factorial (subtract n 1))`.

## What it offers

- **Errors as you type:** syntax, parse and type errors are underlined in the file they belong to, even a module you have not saved yet, and listed in the **Problems** panel. Nothing is run.
- **Grouping and types on hover:** hover over a word to see how its call groups, with brackets around each operand, the type it gives and what it calls. Hover over a variable, parameter or field to see its type.
- **Go to Definition and completion:** jump from any name to its declaration in any file of the program. Completion lists what fits at the cursor and how many operands each call takes, such as `takes 2`.
- **Format Document:** re-indents every line by how the program parses, four spaces per level, and changes nothing else. Line breaks, blank lines and comments stay. Works with format on save.
- **Highlighting that knows your program:** built-in operations, your own functions, variables, parameters, fields, types, variants and module names each get their own colour, and imported names are shown in italics.
- **Editing helpers:** `Ctrl+/` toggles `#` comments, quotes close automatically, `block` … `end` regions fold, and the line after `block` is indented.
- **File icon:** the Pyrop crystal next to `.prp` files in the explorer and on editor tabs, and a **Pyrop Minimal** icon theme for people who like VS Code's Minimal one.

## Getting started

Everything but basic highlighting comes from the `pyrop` command-line tool of the same version as the extension, which the extension starts in the background with `pyrop --lsp`.

On Windows, the installer `pyrop-setup-<version>.exe` puts `pyrop` on your `Path` and can install this extension. Otherwise, set **pyrop.path** to the `pyrop` executable. Without it, `.prp` files are still highlighted from the extension's grammar.

| Setting | Default | Meaning |
| --- | --- | --- |
| `pyrop.path` | `pyrop` | The `pyrop` executable to start |
| `pyrop.languageServer.enabled` | `true` | Turn errors, hover, completion and program-aware highlighting on or off |

Run **Pyrop: Restart Language Server** after installing a new `pyrop`.

To run programs, use `pyrop` itself:

```sh
pyrop hello.prp                 # run a script
pyrop                           # start the REPL
pyrop --build hello.prp         # build a standalone program
pyrop --help                    # everything else
```

## Known limitations

- **Without `pyrop`,** calls to your own functions are coloured like variables and any capitalised word is coloured as a type, because telling them apart needs the whole program.
- **One parse error per declaration:** the rest of a broken function, record or choice cannot be read reliably. Type errors all show once the parse errors are fixed.
- **Untitled files** are checked once they are saved.
- **Italics for imported names** come from a default the extension sets in `editor.semanticTokenColorCustomizations`. If you set that setting yourself, add a rule for `*.imported:pyrop` to keep them.
- **The file icon** shows with icon themes that have no icon of their own for `.prp`, including the default one. VS Code's built-in **Minimal** theme ignores language icons, so the extension comes with **Pyrop Minimal**: the same plain icons plus the crystal for `.prp`. Choose it with **Preferences: File Icon Theme**. The **None** theme shows no icons at all.

## License

Pyrop is released under the MIT License.
