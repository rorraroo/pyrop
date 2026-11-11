# `define`

Declares a variable and gives it its initial value.

```
define <type> <name> <expression>
```

```pyrop
define int age 25
define string name "Ann"
define string greeting add "Hello " name
write_line fill "{} is {}." list_of string greeting convert string age end
```

```output
Hello Ann is 25.
```

- The type is any value type: `int`, `float`, `string`, `bool`, a `list`, `map` or `option` of them, or a record or choice. The initial value must have exactly that type.
- The initial value is required. `define int age` on its own is a parse error.
- The initial value cannot use the variable being declared. Inside a nested block, the same name refers to the outer variable, so `define int x add x 1` reads the outer `x`.
- A name can be declared only once in the same block. An inner `block` may declare the same name again, which hides the outer variable until its `end`.
- A `define` cannot be the whole body of an `if`, `else`, `while` or `for`. Wrap it in `block … end`.
