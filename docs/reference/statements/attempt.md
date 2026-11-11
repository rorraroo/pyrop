# Recoverable errors: `attempt`

Turns the failure of one built-in operation or `convert` into a missing value instead of a runtime error.

```
attempt <built-in operation or convert>
```

```pyrop
use files library
import from files load save end

define string data "level 3"

if_some string text attempt load "notes.txt" write_line text
else write_line "No notes yet."

if not attempt save "save.txt" data write_error_line "Could not save."

define int count value_or attempt convert int read 0
write_line count
```

```input
many
```

```output
No notes yet.
0
```

- The result is `option T` for an operation that gives `T`, and `bool` (`true` when it worked) for `save`, `append` and other operations that give no value.
- **Only the operation itself is caught.** Its arguments are evaluated as usual, so an error inside them, or inside one of your functions, is still a runtime error. Your own functions cannot follow `attempt`; make them return an `option` instead.
- **Library operations work too**, imported or qualified: `attempt load "notes.txt"` or `attempt from files load "notes.txt"`.
- The error message is dropped; write your own.
- `attempt` gives a value, so it cannot be a statement on its own.
