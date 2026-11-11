# 8. When something can go wrong: options and `attempt`

Some values may be missing: the user may type a word where a number was expected, a list may have no item at a position, a key may not be in a map. Pyrop handles this with **options**.

## Options

`option int` is either an `int` or nothing. `some 5` holds a value; `none int` holds none, and names the type it would have held:

```pyrop
define option int found some 5
define option int missing none int
write_line found
write_line missing
```

```output
some 5
none
```

`value_or` gives the held value, or a default when there is none:

```pyrop
define option int missing none int
write_line value_or missing 0
```

```output
0
```

## Using the value: `if_some`

`if_some` runs a statement with the held value in a new variable, and its `else` runs when there is none:

```pyrop
function option int find_age string name
    if equal name "Ann" return some 41
    else return none int

if_some int age find_age "Ann" write_line add age 1
else write_line "Unknown"
if_some int age find_age "Bob" write_line add age 1
else write_line "Unknown"
```

```output
42
Unknown
```

A function that may have no answer returns an option, and whoever calls it must decide what to do when there is none. Nothing is ever silently missing.

When only the yes or no matters, `is_some` and `is_none` ask whether there is a value without reading it:

```pyrop
function option int find_age string name
    if equal name "Ann" return some 41
    else return none int

if is_some find_age "Ann" write_line "We know Ann's age."
if is_none find_age "Bob" write_line "We do not know Bob's age."
```

```output
We know Ann's age.
We do not know Bob's age.
```

## Turning a failure into an option: `attempt`

Many built-in operations can fail: `convert int "abc"`, `get` of a position that is not there, a division by zero. Normally that stops the program. `attempt` in front of the operation gives an option instead: the result if the operation worked, nothing if it failed:

```pyrop
write_line attempt convert int "42"
write_line attempt convert int "forty-two"
write_line attempt get list_of string "a" "b" end 5
```

```output
some 42
none
none
```

Together with `if_some`, this is how a program checks input:

```pyrop
define string answer read
if_some int age attempt convert int answer
    write_line fill "Next year you will be {}." list_of int add age 1 end
else write_line "That is not a number."
```

```input
forty
```

```output
That is not a number.
```

`read` reads one line of input. Here the input is `forty`, so the conversion fails and the `else` runs.

For an operation that gives no value, `attempt` gives a `bool` that says whether it worked. Chapter 9 uses this to save a file.

## What `attempt` catches

`attempt` catches only the failure of the one operation it stands in front of. A failure inside the operation's arguments, or inside one of your own functions, still stops the program:

```pyrop
write_line attempt convert int convert string divide 1 0
```

```error
Division by zero.
```

That keeps real mistakes visible. A bug in your code is never quietly turned into "no value".
