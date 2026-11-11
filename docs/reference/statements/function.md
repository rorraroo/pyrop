# `function`

Defines a function.

```
function <return type> <name> <type> <parameter> <type> <parameter> … <statement>
```

```pyrop
function int square int x
    return multiply x x

function string combine string a string b block
    define string text add a b
    return text
end

write_line square 7
write_line combine "tea" "pot"
```

```output
49
teapot
```

- The parameter list ends at the first word that is not a type name. The body is a single statement; use `block … end` for more than one.
- The return type may be `void` for functions that return nothing. Parameters cannot be `void`.
- Functions can only be defined at the top level of a program, not inside blocks, loops or other functions.
- A function can be called before its definition in the file, and functions can call each other recursively.
- Inside a function you can use its parameters, its own variables, and any global variable of its own file, meaning one declared with `define` directly at the top level of that file. Variables of the calling code are not visible.
- Parameter names must be unique. A `define` in the function's outer block cannot reuse a parameter name.
- Calls can nest at most 10,000 deep. Exceeding this, usually through endless recursion, is a runtime error.
