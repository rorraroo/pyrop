# Quick Reference

## Statements

| Command | Syntax |
| --- | --- |
| `define` | `define <type> <name> <expression>` |
| `set` | `set <name> <expression>` |
| `write` | `write <expression>` |
| `write_line` | `write_line <expression>` |
| `write_error`, `write_error_line` | `write_error <expression>`, to standard error |
| `if` / `else` | `if <condition> <statement> [else <statement>]` |
| `if_some` / `else` | `if_some <type> <name> <option> <statement> [else <statement>]` |
| `while` | `while <condition> <statement>` |
| `for` | `for <type> <name> in <list> <statement>` |
| `break` | `break` |
| `continue` | `continue` |
| `block` / `end` | `block <statements> end` |
| `function` | `function <return type> <name> {<type> <parameter>} <statement>` |
| `record` | `record <Name> <type> <field> {<type> <field>} end` |
| `choice` | `choice <Name> <variant> [<types it carries>] {<variant> [<types it carries>]} end` |
| `match` / `when` | `match <choice value> when <variant> [<names>] <statement> {when …} end`, a `when` for every variant |
| `return` | `return <expression>`, or `return void` in `void` functions |
| `use` | `use <name> "<path>"`, `use <library> library` |
| `import` | `import from <module> <function, record, choice, variant or operation> … end` |
| `export` | `export function …`, `export record …`, `export choice …` |
| `exit` | `exit <int expression>` |
| `write_table` | `write_table <headers> <rows>` |
| `styled` | `styled <style> <statement>` |

## Expressions

| Command | Arity | Result |
| --- | --- | --- |
| `add`, `subtract`, `multiply`, `divide` | 2 | number (`add` also joins strings) |
| `modulo` | 2 | `int` |
| `equal`, `greater`, `less`, `at_least`, `at_most` | 2 | `bool` |
| `smaller_of`, `larger_of` | 2 | same type |
| `length_of` | 1 | `int` |
| `find`, `find_from` | 2, 3 | `option int` |
| `slice`, `get`, `replace` | 3, 2, 3 | `string` |
| `starts_with`, `ends_with` | 2 | `bool` |
| `uppercase`, `lowercase`, `trim` | 1 | `string` |
| `repeat` | 2 | `string` |
| `pad_left`, `pad_right` | 3 | `string` |
| `split` | 2 | `list string` |
| `join`, `fill` | 2 | `string` |
| `and`, `or` | 2 | `bool` |
| `not` | 1 | `bool` |
| `read` | 0 | `string` |
| `ask` | 1 | `string` |
| `confirm` | 1 | `bool` |
| `choose` | 2 | `string` |
| `input_ended` | 0 | `bool` |
| `get` | 2 | the list's element type, the map's value type, or `string` for a text |
| `push`, `put`, `remove` | 2, 3, 2 | the same list or map type |
| `has` | 2 | `bool` |
| `keys_of` | 1 | `list` of the key type |
| `values_of` | 1 | `list` of the value type |
| `positions_of` | 1 | `list int` |
| `slice` | 3 | the same list type |
| `sort`, `reverse`, `distinct` | 1 | the same list type |
| `some` | 1 | `option` of its argument's type |
| `none <type>` | none | `option` of the named type |
| `value_or` | 2 | the option's value type |
| `is_some`, `is_none` | 1 | `bool` |
| `attempt <operation>` | the operation's arity | `option` of its result, or `bool` |
| `convert <type>` | 1 | the named type |
| `map_of <key type> <value type> … end` | any number of key and value pairs, up to `end` | `map` of the named types |
| `variants_of <choice>` | 0 | `list` of the choice |
| `list_of <type> … end` | any number of items, up to `end` | `list` of the named type |
| user function | its parameter count | its return type |
| `from <module> <function>` | the function's parameter count | its return type |
| `from <library> <operation>`, or an imported one | its arity in [the library's table](statements/libraries.md) | its result |
| record constructor `<Record>` | its field count | the record |
| labelled constructor `<Record> with <field> <value> …` | one `with` per field | the record |
| `the <field> of` | 1 | the field's type |
| `copy <record> with <field> <value> … end` | a record, then values up to `end` | the same record type |
| variant `<variant>`, `from <module> <variant>` | the number of values it carries, often none | its choice |

## Reserved words

`define` `set` `if` `else` `while` `for` `in` `break` `continue` `block` `end` `function` `return` `write` `write_line` `write_error` `write_error_line` `exit` `styled` `use` `import` `export` `from` `library` `record` `choice` `the` `of` `copy` `with` `convert` `list_of` `map_of` `variants_of` `attempt` `if_some` `match` `when` `none` `true` `false` `int` `float` `string` `bool` `void` `list` `map` `option` `add` `subtract` `multiply` `divide` `modulo` `equal` `greater` `less` `and` `or` `not` `read` `ask` `confirm` `choose` `write_table` `input_ended` `get` `has` `remove` `at_least` `at_most` `smaller_of` `larger_of` `length_of` `find` `find_from` `replace` `starts_with` `ends_with` `uppercase` `lowercase` `trim` `repeat` `pad_left` `pad_right` `split` `join` `fill` `push` `put` `keys_of` `values_of` `positions_of` `slice` `sort` `reverse` `distinct` `some` `value_or` `is_some` `is_none`

These words were removed from the language and are free to use as names. Where a file does not use one as a name, using it says what replaced it: `empty` (use `list_of T end` or `map_of K V end`), `item_at`, `value_of` and `char_at` (use `get`), `contains` and `has_key` (use `has`), `remove_at` and `remove_key` (use `remove`), `replace_at` (use `put`), `negate` (use `subtract 0 x`), `get_field` (use `the <field> of <record>`), `with_field` (use `copy <record> with <field> <value> end`), `with_style` (use `styled`), `substring` (use `slice`), `upper` and `lower` (use `uppercase` and `lowercase`), `smaller` and `larger` (use `smaller_of` and `larger_of`), and `def` (use `define`).

`call`, `function_type`, `function_of` and `at_once` are reserved for a later version of Pyrop. They do nothing yet and cannot be used as names.

The names of the built-in libraries and of their operations are not reserved ([Built-in libraries](statements/libraries.md)).
