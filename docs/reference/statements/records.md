# Records: `record`, `the … of` and `copy … with … end`

Declares a type of your own with named fields.

```
record <Name> <type> <field> <type> <field> … end
the <field> of <record>
copy <record> with <field> <new value> with <field> <new value> … end
```

```pyrop
record Point float x float y end
record Hero string name int health Point position list string inventory end

define Hero hero Hero "Ann" 10 Point 0.0 0.0 list_of string end
write_line the health of hero
set hero copy hero with health subtract the health of hero 1 end
write_line hero        # Hero name "Ann" health 9 position (Point x 0.0 y 0.0) inventory []
```

```output
10
Hero name "Ann" health 9 position (Point x 0.0 y 0.0) inventory []
```

- **The record's name** is any name that is not reserved, such as `Point`, `hero_stats` or `čas`. Capitalising it, as in `Point` or `Hero`, is a convention that keeps the lowercase noun free for a variable: `define Hero hero …`. A record shares its name with nothing else in the file, so `define hero hero …` after `record hero …` is an error.
- **The fields** are a type and a name each, like function parameters, and end at `end`, so they can span several lines. A record needs at least one field. Two fields of one record cannot share a name, and a field cannot be named like a reserved word.
- **The record's name is also its constructor:** `Point 1.0 2.0` makes a new `Point` with one value per field, in order. It works anywhere a value is expected.
- **Naming the fields:** `Point with x 1.0 with y 2.0` makes the same record, with one `with <field> <value>` per field. Every field must appear exactly once, in any order, and the values run in the order written. Use it when a record has many fields, where two values of one type are easy to swap.
- **A constructor cannot stand alone as a statement.** It would make a record and throw it away, and after a function's parameters it would be read as one more parameter.
- **`the <field> of <record>`** reads one field, and nests: `the name of the owner of item`. **`copy <record> with <field> <value> end`** gives a new record with that field changed; the old record stays as it was, so change a variable with `set`. Change several fields with more `with`s before the `end`: `copy hero with x 1 with y 2 end`. Each field can be changed only once in one `copy`. Every `copy` closes with its own `end`, so one `copy` can sit inside another: `copy item with owner copy the owner of item with name "Bo" end end`.
- **Records are values,** like lists: `define Point b a` makes a copy, and changing `b` never changes `a`.
- **A record cannot contain itself directly**, because no such value could ever be made. Inside a list, a map or an option it can: `record Tree string label list Tree children end`, `record Room string name option Room north end`.
- **`equal`** compares records field by field. **Printing** a record, or `convert string`, shows its name and fields: `Point x 1.0 y 2.0`. A record held in another record's field is shown in brackets: `Line from (Point x 0.0 y 0.0) to (Point x 1.0 y 1.0)`.
- **Records cannot be map keys.** They can be map values, list items and fields of other records.
- Records are declared only at the top level, and can be used anywhere in the file, even above their declaration.
- **In other files:** `export record` makes a record usable from other modules, as `from <module> <Record>`, both as a type and as a constructor, or after `import from <module> <Record> end` by its name alone. Two modules' records with the same name are different types.
- In the REPL, a record stays declared for later inputs. Repeating it with the same fields is allowed; changing its fields is an error.
