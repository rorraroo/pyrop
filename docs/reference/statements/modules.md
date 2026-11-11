# Modules: `use`, `from`, `import` and `export`

Splits a program into files. Every file is a module with its own functions and variables.

```
use <name> "<path>"
from <name> <function>
import from <name> <function, record, choice or variant> … end
export function …
export record …
export choice …
```

```prp lib/shapes.prp
# lib/shapes.prp
export function float circle_area float r
    return multiply pi multiply r r

define float pi 3.14159
```

```pyrop
# main.prp
use shapes "lib/shapes.prp"
import from shapes circle_area end

write_line from shapes circle_area 2.0
write_line circle_area 1.0
```

```output
12.56636
3.14159
```

- **`use`** loads a file as a module and gives it a name in this file. You choose the name; it comes first, then the path.
- **`from <module> <function>`** calls a function of a module. The three words act like one function name, so `from shapes circle_area 2.0` takes one argument because `circle_area` does. `from <module> <Record>` names an exported record, as a type or as its constructor, and `from <module> <Choice>` and `from <module> <variant>` name an exported choice and its variants.
- **`import from <module> <name> … end`** lets you leave out `from <module>` for each function, record, choice or variant it names, anywhere in this file: `import from world Direction Place Room end`. The names end at `end`, even when there is only one. Importing a choice imports only the type; each variant you want without `from <module>` is named too.
- **`export`** in front of `function`, `record` or `choice` makes it usable from other files. Functions, records and choices without it are private to their file. In a module, an exported function or record cannot use a private record or choice of its file in its parameters, return type or fields.
- **Variables stay in their file.** A module's global variables cannot be read or changed from other files, only through its functions. Two files can have functions and variables with the same names.
- **The path must be a string literal.** Pyrop reads modules before the program runs, so a path held in a variable is not allowed. Relative paths are resolved from the folder of the file that contains the `use`; in the REPL, from the current folder.
- **A module's functions can be called anywhere** in the file that uses it, even above the `use` or `import` line.
- **A module's own statements**, such as `write_line` or `define` at its top level, run once, before the code of the file that uses it. A file used by several modules, or under several names, is still loaded and run only once.
- **Modules cannot use each other in a circle.** If `a.prp` uses `b.prp` and `b.prp` uses `a.prp`, that is an error; move what they share into a third file.
- **Names must not clash** within a file: an imported name, a function, a record, a choice, a variant and a variable cannot share a name. Module names are apart: a module name is only written after `use`, `from` or `import from`, so `use hero "hero.prp"` and a parameter `Hero hero` can live in one file. Two modules cannot share a name.
- `use`, `import` and `export` are only allowed at the top level of a file, not inside blocks, loops or functions.
- **Errors in a module** are reported with that file's name and line. A file that cannot be found is reported at the `use`.
