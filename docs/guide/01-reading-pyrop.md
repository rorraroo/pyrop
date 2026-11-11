# 1. Reading Pyrop

Pyrop has no brackets, no commas and no operator symbols such as `+` or `=`. A program is made of words, numbers and text in quotes. That makes it easy to read aloud and easy to type, and it takes a little practice to read with your eyes. This chapter is that practice, and every later chapter depends on it.

## Every operation comes first

In most languages you write `1 + 2`. In Pyrop the operation comes first and its arguments follow:

```pyrop
write_line add 1 2
```

```output
3
```

Read it from the left: `write_line` writes one value on a line of its own. Which value? `add 1 2`, which adds two numbers. So the program writes `3`.

## Each operation takes a fixed number of arguments

This is the one rule that makes brackets unnecessary. `write_line` always takes one argument, `add` always takes two, `multiply` always takes two. Pyrop knows these counts, so it always knows where each part ends:

```pyrop
write_line multiply add 1 2 subtract 10 4
```

```output
18
```

To read it, fill the slots from the left:

1. `write_line` needs 1 value. The next word is `multiply`, so the value is a multiplication.
2. `multiply` needs 2 values. The first starts with `add`.
3. `add` needs 2 values: `1` and `2`. That is the first value of `multiply`: 3.
4. The second value of `multiply` starts with `subtract`, which needs 2 values: `10` and `4`. That gives 6.
5. `multiply` now has both values, 3 and 6, and gives 18.

Read aloud, it is "write line: multiply (add 1 and 2) by (subtract 4 from 10)". With brackets, it would be `write_line(multiply(add(1, 2), subtract(10, 4)))`. Pyrop leaves the brackets out because the counts already say where they would go.

## Line breaks do not matter

A line break is just a space. You can spread one statement over several lines, which helps when an expression gets long:

```pyrop
write_line multiply
    add 1 2
    subtract 10 4
```

```output
18
```

You can also put several statements on one line. Pyrop knows where each one ends, because the first word of a statement says how many parts follow:

```pyrop
write_line 1 write_line 2
```

```output
1
2
```

Most programs put one statement on each line, because that is easiest to read.

## When the count is wrong

Give an operation one value too many, and the extra value has nowhere to go:

```pyrop
write_line add 1 2 3
```

```error
'3' cannot stand alone as a statement. This usually means the expression before it was given more arguments than its operation takes.
```

`add` took `1` and `2`, `write_line` was finished, and then `3` started a new statement, which a number cannot do. Pyrop reports such mistakes before running anything.

When an error points at a place that looks fine, the mistake is often earlier: an operation took one argument fewer or more than you meant. To see how Pyrop grouped your program, run it with `--tree`. It shows every call with brackets added and runs nothing:

```
pyrop --tree main.prp
```

```
write_line (multiply (add 1 2) (subtract 10 4))
```

In VS Code with the Pyrop extension you can see the same without leaving the editor: hover over `multiply` and it shows `multiply (add 1 2) (subtract 10 4)` and the type it gives. **Format Document**, or `pyrop --format main.prp`, indents every line by how it parses, so a body that is not where you thought it was stands out.

## Comments

`#` starts a comment, which lasts to the end of the line. Pyrop ignores it:

```pyrop
# Work out the area of a 3 by 4 rectangle.
write_line multiply 3 4    # 12
```

```output
12
```

## Running a program

Save a program in a file whose name ends in `.prp`, such as `hello.prp`, and run it:

```
pyrop hello.prp
```

Running `pyrop` alone starts the REPL, where you type statements and see their results at once. It is a good place to try the examples in this guide.

Next: [Values, variables and output](02-values-and-variables.md)
