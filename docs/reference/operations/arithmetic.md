# Arithmetic

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `add` | 2 | `int int`, `float float`, `string string` | same type | `add 2 3` → `5`, `add "a" "b"` → `"ab"` |
| `subtract` | 2 | `int int`, `float float` | same type | `subtract 10 4` → `6` |
| `multiply` | 2 | `int int`, `float float` | same type | `multiply 2.5 2.0` → `5.0` |
| `divide` | 2 | `int int`, `float float` | same type | `divide 7 2` → `3`, `divide 7.0 2.0` → `3.5` |
| `modulo` | 2 | `int int` | `int` | `modulo 7 3` → `1` |

- `add` on two strings joins them.
- `divide` on ints drops the fractional part, rounding toward zero.
- Dividing by zero with `divide` or `modulo` is a runtime error for both ints and floats.
- Int results that do not fit in 64 bits are a runtime error, not a silent wraparound. The only such `divide` is `divide -9223372036854775808 -1`.
- `modulo` gives a result with the sign of its first operand: `modulo -7 2` is `-1`.
- Float results that are too large to represent are a runtime error.
