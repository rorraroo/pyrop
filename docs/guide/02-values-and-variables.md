# 2. Values, variables and output

## Four kinds of simple values

| Type | What it holds | Examples |
| --- | --- | --- |
| `int` | whole numbers | `42`, `-7`, `0` |
| `float` | numbers with a fraction | `3.14`, `-0.5`, `2.0` |
| `string` | text | `"Hello"`, `""` |
| `bool` | true or false | `true`, `false` |

A `float` always has digits on both sides of its point, so `2.0` and never `2.`.

## Variables

`define` makes a variable. It takes three parts: the type, the name, and the first value.

```pyrop
define string name "Ann"
define int age 41
write_line name
write_line age
```

```output
Ann
41
```

`set` gives a variable a new value of the same type:

```pyrop
define int score 10
set score add score 5
write_line score
```

```output
15
```

Every variable starts with a value, so there is no such thing as a variable that holds nothing yet.

## Types never change by themselves

Pyrop checks every type before the program runs. An `int` and a `float` are different types, and adding them is a mistake:

```pyrop
write_line add 1 2.0
```

```error
'add' cannot take int float. It accepts: int int, float float, string string.
```

The message lists what `add` does accept. When you mean a conversion, say so with `convert`, which takes the type you want and the value:

```pyrop
write_line add 1.0 convert float 2
```

```output
3.0
```

## Arithmetic

`add`, `subtract`, `multiply` and `divide` each take two numbers of the same type. Dividing two ints gives an int and drops the remainder; `modulo` gives the remainder:

```pyrop
write_line divide 7 2
write_line modulo 7 2
write_line divide 7.0 2.0
```

```output
3
1
3.5
```

Floats are always written with a point, so you can tell `3.0` from `3` in the output.

## Text

`add` also joins two strings. To put a number into text, convert it first:

```pyrop
define int age 41
write_line add "Age: " convert string age
```

```output
Age: 41
```

When there are several values to put into a text, `fill` is easier. It replaces each `{}` with the next item of a list:

```pyrop
define string name "Ann"
define int age 41
write_line fill "{} is {} years old." list_of string name convert string age end
```

```output
Ann is 41 years old.
```

`list_of string … end` is a list of strings. Lists have their own chapter; for now, read it as "these values, up to `end`". It needs an `end` because, unlike an operation, it can hold any number of items.

Some other text operations:

```pyrop
write_line uppercase "quiet"
write_line length_of "hello"
write_line trim "  spaced out  "
write_line replace "a cat" "cat" "dog"
```

```output
QUIET
5
spaced out
a dog
```

## `write` and `write_line`

`write_line` ends its output with a line break; `write` does not, so the next output continues on the same line:

```pyrop
write "Loading"
write "..."
write_line " done"
```

```output
Loading... done
```
