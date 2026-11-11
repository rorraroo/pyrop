# `while`

Repeats a statement while a condition is `true`. The condition is checked before every iteration.

```
while <condition> <statement>
```

```pyrop
define int i 0
while less i 3 block
    write_line i
    set i add i 1
end
```

```output
0
1
2
```

The condition must be a `bool`. The body is a single statement; use `block … end` for more than one.
