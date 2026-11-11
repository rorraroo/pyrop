# `ask` and `confirm`

Ask the user a question.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `ask` | 1 | `string` prompt | `string` | `ask "Name: "` → what the user typed |
| `confirm` | 1 | `string` prompt | `bool` | `confirm "Delete it?"` → `true` for yes |

```pyrop
use files library

define string name ask "Name: "
if confirm "Save the report?" from files save "report.txt" add "Author: " name
if from files file_exists "report.txt" write_line from files load "report.txt"
```

```input
Ann
y
```

```output
Name: 
Save the report? [y/n] 
Author: Ann
```

- `ask` shows the prompt exactly as written, so include your own `": "`. `ask "Name: "` is the same as `write "Name: "` followed by `read`.
- `confirm` adds ` [y/n] ` after the prompt. It accepts `y`, `yes`, `n` and `no` in any case, and asks again after any other answer.
- On a terminal the prompt starts with a `?` marker. In plain mode it is printed as plain text.
- Both read the same input as `read`, so answers can come from a file: `pyrop setup.prp < answers.txt`. Running out of input is a runtime error.
