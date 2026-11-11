# Choices: `choice`

Declares a type with a fixed set of named values, its variants.

```
choice <Name> <variant> [<type>…] <variant> [<type>…] … end
```

```pyrop
choice Direction north east south west end

define Direction facing north
if equal facing west write_line "The sun sets ahead."
define map Direction string exits map_of Direction string south "cellar" end
write_line convert Direction "east"
```

```output
east
```

- **The choice's name** is any name that is not reserved, like a record's, and is capitalised by convention. The variants are ordinary names in any style, and the list ends at `end`. A choice needs at least one variant.
- **Each variant is a value** of its choice, written as a bare word wherever a value is expected: `define Direction facing north`. The choice's name is a type, never a value.
- **`variants_of <Choice>`** gives every variant as a list, in declaration order: `variants_of Direction`. A choice where any variant carries data cannot be listed, and is an error before the program runs.
- **Variants are names of their file**, like functions: a variant cannot share its name with a function, record, choice, variable, imported name, or a variant of another choice in the same file.
- **`equal`** compares variants, and for variants with data the data too: `equal go north go north` is `true`. Choices have no order, so `greater` and `less` do not take them.
- **`convert string`** gives the variant's text form, its name followed by any data; **`convert Direction "north"`** turns a name back into a variant. Spaces around the text are ignored and case matters; a text that names no variant is a runtime error.
- **Printing** a variant shows its name and any data, also inside lists, maps and records: `[north, east]`, `[go north, wait]`.
- **A variant can carry data**: the types written after it, up to the next word that is not a type. `choice Action go Direction take string wait end` makes `go` take one `Direction`, so `go north` is an `Action`, and `wait` stays a plain value. Read the data with `match`. Printing shows the data after the name: `go north`, `take "lamp"`.
- **Choices can be map keys**, list items, map values and record fields. A choice with data cannot be a map key. It converts from text like a record ([Conversions](../conversions.md)).
- Choices are declared only at the top level, and can be used anywhere in the file, even above their declaration.
- **In other files:** `export choice` makes a choice and all its variants usable from other modules: the type as `from <module> <Choice>` and each variant as `from <module> <variant>`. `import from <module> <Choice> end` imports the type only. Each variant to be written without `from <module>` is named too: `import from items Item sword end` imports the type and `sword`, and `import from items sword end` imports just the variant. A module can then add a variant without taking a name from your file. Two modules' choices with the same name are different types.
- In the REPL, a choice stays declared for later inputs. Repeating it with the same variants is allowed; changing its variants is an error.
