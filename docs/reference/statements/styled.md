# `styled`

Runs a statement with a style applied to everything it writes.

```
styled <style> <statement>
```

```pyrop
styled "success" write_line "Saved."
styled "warning" block
    write "Only "
    styled "emphasis" write "3%"
    write_line " of the disk is free."
end
```

```output
Saved.
Only 3% of the disk is free.
```

| Style | Meaning | On a terminal |
| --- | --- | --- |
| `"emphasis"` | important | bold |
| `"muted"` | less important | grey |
| `"success"` | something worked | green |
| `"warning"` | needs attention | yellow |
| `"danger"` | something failed | red |
| `"info"` | neutral information | cyan |

- The style is a string. A misspelled style in quotes is reported before the program runs; a misspelled style in a variable is a runtime error.
- An inner `styled` replaces the outer style until it finishes.
- In plain mode styles are left out and the text prints as it is.
- Styles are named by meaning, never by colour. Colours by name, such as `red` for a dragon, are decoration and exist only in views: see `in_color` and `colored_grid` in [the library `terminal`](../operations/terminal.md).
