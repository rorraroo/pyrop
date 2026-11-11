# `return`

Ends a function and gives back a value. A `void` function gives back nothing, so it writes `return void`.

```
return <expression>
return void
```

```pyrop
function int sign int x
    if greater x 0 return 1
    else if less x 0 return -1
    else return 0

write_line sign -5
```

```output
-1
```

- The value's type must match the function's return type.
- A non-`void` function must return a value on **every path**. This is checked before the program runs. A path is covered if:
  - it reaches a `return` or an [`exit`](exit.md), or
  - it is a `block` with such a statement in it, or
  - it is an `if` or `if_some` with an `else` whose branches both return, or
  - it is a [`match`](match.md) whose branches all return, or
  - it is a `styled` whose statement returns, or
  - it is a `while true` loop without a `break`.
- A call never covers a path, even when the function it calls always exits. Write `exit` or `return` after it.
- Nothing can follow a `return` in the same `block`, because it could never run; that is an error before the program starts.
- `return` is only allowed inside a function. In a `void` function it must be `return void`, and `return void` is not allowed anywhere else.
- A `void` function also ends when its body finishes, so `return void` is only needed to leave early:

  ```
  function void greet string name
  block
      if equal name "" return void
      write_line add "Hi " name
  end
  ```
