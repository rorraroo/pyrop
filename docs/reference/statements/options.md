# Optional values: `option`, `some`, `none`, `value_or`, `is_some` and `is_none`

A value of type `option T` holds a value of type `T`, or nothing.

```
some <expression>
none <type>
value_or <option> <default>
is_some <option>
is_none <option>
```

```pyrop
record Room string name int floor end
define list Room rooms list_of Room Room "Hall" 0 Room "Attic" 2 end

function option Room find_room string wanted
block
    for Room room in rooms if equal the name of room wanted return some room
    return none Room
end

write_line value_or find_room "Attic" Room "Nowhere" 0
```

```output
Room name "Attic" floor 2
```

- **`some 5`** is an `option int` holding 5. **`none int`** is an `option int` holding nothing.
- **`value_or <option> <default>`** gives the value, or the default when there is none. The default is only evaluated when it is needed.
- **`is_some <option>`** is `true` when the option holds a value, and **`is_none <option>`** when it holds nothing. They only answer yes or no, so use them when the value itself is not needed.
- Read the value with [`if_some`](if-some.md) or `value_or`; there is no other way, so a missing value is never read by mistake.

```pyrop
function option int find_number string text
    return attempt convert int text

if is_some find_number "42" write_line "42 is a number."
if is_none find_number "forty" write_line "forty is not."
```

```output
42 is a number.
forty is not.
```

- Options nest and can go anywhere a type can: `list option int`, `option option int`, a record field `option Room` (a record may even hold itself this way). They cannot be map keys.
- `equal` compares options: two are equal when both hold nothing, or both hold equal values.
- **Printing** shows `some 5` or `none`; texts inside are quoted, `some "Ann"`, and a record inside is bracketed, `some (Point x 1.0 y 2.0)`.
