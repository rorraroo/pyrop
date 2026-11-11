# Text

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `length_of` | 1 | `string` | `int` | `length_of "čau"` → `3` |
| `slice` | 3 | `string` text, `int` start, `int` count | `string` | `slice "Pyrop rocks" 6 5` → `"rocks"` |
| `get` | 2 | `string` text, `int` position | `string` | `get "Pyrop" 0` → `"P"` |
| `find` | 2 | `string` text, `string` searched | `option int` | `find "hello" "l"` → `some 2`, `find "hello" "z"` → `none` |
| `find_from` | 3 | `string` text, `string` searched, `int` start | `option int` | `find_from "a-b-c" "-" 2` → `some 3` |
| `replace` | 3 | `string` text, `string` old, `string` new | `string` | `replace "a-b-c" "-" "+"` → `"a+b+c"` |
| `starts_with` | 2 | `string string` | `bool` | `starts_with "Pyrop" "Py"` → `true` |
| `ends_with` | 2 | `string string` | `bool` | `ends_with "notes.txt" ".txt"` → `true` |
| `uppercase` | 1 | `string` | `string` | `uppercase "čau"` → `"ČAU"` |
| `lowercase` | 1 | `string` | `string` | `lowercase "ČAU"` → `"čau"` |
| `trim` | 1 | `string` | `string` | `trim "  hi  "` → `"hi"` |
| `repeat` | 2 | `string` text, `int` count | `string` | `repeat "ab" 3` → `"ababab"` |
| `pad_left` | 3 | `string` text, `int` width, `string` fill | `string` | `pad_left "7" 3 "0"` → `"007"` |
| `pad_right` | 3 | `string` text, `int` width, `string` fill | `string` | `pad_right "ab" 4 "."` → `"ab.."` |
| `split` | 2 | `string` text, `string` separator | `list string` | `split "a,b,,c" ","` → `["a", "b", "", "c"]` |
| `join` | 2 | `list string` items, `string` separator | `string` | `join list_of string "a" "b" end ", "` → `"a, b"` |
| `fill` | 2 | `string` template, `list T` items | `string` | `fill "{} of {}" list_of int 3 10 end` → `"3 of 10"` |

- **Characters are counted as you see them.** Accented letters and emoji count as one character each, so `pad_left` and `pad_right` line up columns.
- **Positions are numbered from 0.** A position or count that goes outside the text is a runtime error.
- **Searching is exact**: `find "Hello" "h"` is `none`. Use [`if_some`](../statements/if-some.md) to act on what was found. `replace` replaces every match.
- `get` gives a one-character `string`.
- The fill for `pad_left` and `pad_right` must be exactly one character. Text that is already long enough stays unchanged.
- **`split`** keeps empty pieces, so `join` with the same separator gives back the original text. Text without the separator, even `""`, gives a list with one item. An empty separator is a runtime error. A Windows line break `\r\n` counts as one character, so `split text "\n"` does not cut text that contains `\r\n`, such as text from an argument or an environment variable; split at `"\r\n"` instead. Text read from files always uses `\n`.
- **`join`** of an empty list is `""`. It is the quick way to build a long text: collect the pieces in a list, then join them once.
- **`fill`** puts values into a sentence: each `{}` in the template becomes the next item, written as `convert string` would write it, so numbers and variants need no `convert`. Write `{{` or `}}` for a brace of its own. The number of `{}` must equal the number of items, and a brace on its own is an error; both are runtime errors that `attempt` catches. A list holds one type, so for a sentence with mixed values, convert the others to `string`: `fill "{} has {} gold" list_of string name convert string gold end`.

```pyrop
define string line trim read
if starts_with line "#" write_line "comment"
else write_line add pad_left convert string length_of line 4 " " add " | " line
```

```input
  hello world  
```

```output
  11 | hello world
```
