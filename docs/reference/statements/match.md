# Handling every variant: `match` and `when`

Runs the branch for the variant a choice value holds, with the variant's data in named variables.

```
match <choice value>
    when <variant> <name>… <statement>
    …
end
```

```pyrop
choice Direction north south east west end
choice Action go Direction take string wait end

define Action action go north

match action
    when go way write_line fill "You go {}." list_of Direction way end
    when take item write_line add "You take the " item
    when wait write_line "You wait."
end
```

```output
You go north.
```

- **Every variant needs exactly one `when`.** A missing variant, a variant handled twice, or a variant of another choice is an error before the program runs, so adding a variant shows you every `match` that must handle it.
- **Write one name per value the variant carries**, without types: `when go way`. The names exist only in that branch.
- **Each branch is one statement**; use `block … end` for several. The `match` ends with `end`.
- A function returns on every path through a `match` when every branch returns, so no extra `return` is needed after it.
- `when` can also name a variant of another module: `when from moves go way …`.
