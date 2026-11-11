# Language Basics

## Prefix notation without brackets

Every command starts with its name, followed by its arguments, separated by whitespace:

```pyrop
write_line add 10 20
```

```output
30
```

Pyrop has no brackets. Each operation takes a **fixed number of arguments**, and the interpreter uses those counts to work out how expressions nest:

```pyrop
write_line multiply add 1 2 subtract 10 4    # (1 + 2) * (10 - 4) = 18
```

```output
18
```

## Whitespace and comments

- Spaces, tabs and newlines only separate words. Line breaks and indentation have no meaning, so a statement can span several lines, and several statements can share one line.
- `#` starts a comment that runs to the end of the line.

```pyrop
define int x 5 set x 6 write_line x    # three statements on one line
```

```output
6
```

## Types and literals

| Type | Meaning | Literals |
| --- | --- | --- |
| `int` | 64-bit signed integer, from `-9223372036854775808` to `9223372036854775807` | `42`, `-7` |
| `float` | 64-bit floating-point number | `3.14`, `-0.5` (digits are required on both sides of the `.`) |
| `string` | Text | `"Hello"` |
| `bool` | Truth value | `true`, `false` |
| `void` | No value; only used as a function return type and in `return void` | none |
| `list T` | A list of values of type `T`, numbered from 0 | `list_of int 1 2 3 end`, `list_of string end` |
| `map K V` | Keys of type `K` (`int`, `string`, `bool` or a choice) with values of type `V` | `map_of string int end` |
| a record | A type you declare with `record`, holding named fields | `Point 1.0 2.0` (its constructor) |
| a choice | A type you declare with `choice`, with a fixed set of named variants | `north` (one of its variants) |
| `option T` | A value of type `T`, or no value | `some 5`, `none int` |

A negative number needs a space or a line break before its `-`: `add 5 -3` is 2, while `add 5-3` is an error, because Pyrop has no subtraction sign and would otherwise read it as `5` and `-3` without a word. Subtract with `subtract 5 3`.

Collection types nest without brackets: `list list int` is a list of lists of ints, and `map string list int` maps strings to lists of ints. See [Lists and maps](operations/lists-and-maps.md). Record and choice types are written with their name, such as `list Point`; see [Records](statements/records.md) and [Choices](statements/choices.md).

Escape sequences in string literals:

| Escape | Meaning |
| --- | --- |
| `\"` | Double quote |
| `\\` | Backslash |
| `\n` | Line break |
| `\r` | Carriage return |
| `\t` | Tab |

A string must end on the line it starts on; use `\n` for line breaks.

Typing is strict. Values never change type implicitly; use [`convert`](conversions.md) instead. For example, `add 1 2.0` is a type error.

## Names

Variable, parameter and function names consist of letters, digits and `_`, and cannot start with a digit. Names are case-sensitive. Reserved words cannot be used as names: every keyword, type name and core built-in operation in this reference. The operations of the [built-in libraries](statements/libraries.md) are not reserved: `round` or `seed` is a fine variable name unless the file imports that operation. A variable, a function, a record, a choice and a variant cannot share a name, and none of them can share one with a module name or an imported name of the same file. Record and choice names follow the same rules; they are capitalised by convention, as in `Point`, so the lowercase noun stays free for a variable.

Pyrop's own names that consist of several words are joined with underscores, such as `write_line` and `length_of`. If you leave the underscores out, the error suggests the right spelling: "did you mean 'write_line'?". In versions before 0.6, `write_line` was spelled `writeline`.
