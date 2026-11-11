# Comparison

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `equal` | 2 | any two values of the same type, lists and maps by content | `bool` | `equal "a" "a"` → `true` |
| `greater` | 2 | `int int`, `float float`, `string string` | `bool` | `greater 5 3` → `true` |
| `less` | 2 | `int int`, `float float`, `string string` | `bool` | `less 1.5 0.5` → `false` |
| `at_least` | 2 | `int int`, `float float`, `string string` | `bool` | `at_least 3 3` → `true` (≥) |
| `at_most` | 2 | `int int`, `float float`, `string string` | `bool` | `at_most 4 3` → `false` (≤) |
| `smaller_of` | 2 | `int int`, `float float`, `string string` | same type | `smaller_of 3 7` → `3` |
| `larger_of` | 2 | `int int`, `float float`, `string string` | same type | `larger_of 2.5 1.5` → `2.5` |

Strings are compared character by character, by character code.
