# 4. Functions

## Defining a function

A function has a return type, a name, its parameters (each a type and a name) and one statement as its body:

```pyrop
function int square int x
    return multiply x x

write_line square 5
```

```output
25
```

`square` takes one parameter, so a call of `square` takes one argument, exactly like a built-in operation. Pyrop reads the parameters from the definition, which is how it knows where `square 5` ends. A function may be called above its definition.

## More than one statement

The body is one statement; use `block … end` for more:

```pyrop
function string greeting string name int hour block
    define string part "Good evening"
    if less hour 12 set part "Good morning"
    else if less hour 18 set part "Good afternoon"
    return fill "{}, {}!" list_of string part name end
end

write_line greeting "Ann" 9
write_line greeting "Bob" 20
```

```output
Good morning, Ann!
Good evening, Bob!
```

The list of parameters ends at the first word that is not a type: here `int hour` is followed by `block`, so there are two parameters.

## Every path returns

A function that gives a value must return one on every path. Pyrop checks this before running:

```pyrop
function string size_of int n
    if greater n 100 return "large"
```

```error
Function 'size_of' must return a value of type string on every path.
```

Adding an `else` covers the other path:

```pyrop
function string size_of int n
    if greater n 100 return "large"
    else return "small"

write_line size_of 7
```

```output
small
```

## Functions that give nothing back

A function whose return type is `void` does something instead of computing a value. Its call is a statement on its own:

```pyrop
function void banner string title block
    write_line repeat "=" length_of title
    write_line title
    write_line repeat "=" length_of title
end

banner "Report"
```

```output
======
Report
======
```

To leave a `void` function early, write `return void`.

## Functions calling themselves

A function may call itself, as long as it stops eventually:

```pyrop
function int factorial int n
    if less n 2 return 1
    else return multiply n factorial subtract n 1

write_line factorial 10
```

```output
3628800
```

Read the last line slot by slot: `multiply` takes `n` and `factorial …`, and `factorial` takes `subtract n 1`.

## What a function can see

Inside a function you can use its parameters, its own variables, and the variables declared at the top level of the file. Variables of the code that calls it are not visible, so a function depends only on what it is given and on the file's own globals.
