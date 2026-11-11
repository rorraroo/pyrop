# 3. Making decisions and repeating

## `if` and `else`

`if` takes a condition and one statement to run when the condition is true. `else` gives the statement for when it is false:

```pyrop
define int temperature 25
if greater temperature 20 write_line "Warm"
else write_line "Cool"
```

```output
Warm
```

The condition must be a `bool`. These operations compare two values and give one:

| Operation | True when |
| --- | --- |
| `equal a b` | a and b are the same |
| `greater a b` | a is greater than b |
| `less a b` | a is less than b |
| `at_least a b` | a is greater than or equal to b |
| `at_most a b` | a is less than or equal to b |

`and`, `or` and `not` combine conditions:

```pyrop
define int age 15
define bool with_parent true
if or at_least age 18 with_parent write_line "You may enter."
```

```output
You may enter.
```

Chains of choices are written with `else if`:

```pyrop
define int score 72
if at_least score 90 write_line "Excellent"
else if at_least score 60 write_line "Passed"
else write_line "Try again"
```

```output
Passed
```

## Several statements: `block` … `end`

`if` runs exactly one statement. When you need more, group them with `block`, which ends with `end`:

```pyrop
define int lives 1
if equal lives 1 block
    write_line "Careful!"
    write_line "This is your last life."
end
```

```output
Careful!
This is your last life.
```

A variable made with `define` inside a block exists only until its `end`.

## Repeating with `while`

`while` runs its statement again and again for as long as its condition is true:

```pyrop
define int count 1
while at_most count 3 block
    write_line count
    set count add count 1
end
```

```output
1
2
3
```

## Going through a list with `for`

`for` runs its statement once for each item of a list. You give the type and the name of the loop variable, then `in` and the list:

```pyrop
for string colour in list_of string "red" "green" "blue" end
    write_line uppercase colour
```

```output
RED
GREEN
BLUE
```

## Leaving early: `break` and `continue`

`break` ends the loop at once. `continue` skips the rest of this round and goes on with the next one:

```pyrop
for int number in list_of int 1 2 3 4 5 6 end block
    if equal modulo number 2 1 continue
    if greater number 4 break
    write_line number
end
```

```output
2
4
```

`while true` with a `break` inside is a common way to write a loop whose end is found in the middle:

```pyrop
define int guess 1
while true block
    if equal multiply guess guess 49 break
    set guess add guess 1
end
write_line guess
```

```output
7
```
