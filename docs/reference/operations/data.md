# JSON and YAML: the library `data`

These come from the [library](../statements/libraries.md) `data`: add `use data library`.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `parse_json` | 1 | `string` | `Data` | `parse_json "[1, 2]"` |
| `parse_yaml` | 1 | `string` | `Data` | `parse_yaml "keep: 3"` |
| `json_text` | 1 | `Data` | `string` | `json_text document` |
| `yaml_text` | 1 | `Data` | `string` | `yaml_text document` |
| `text_at` | 2 | `Data`, `string` path | `string` | `text_at document "items.0.name"` |
| `int_at` | 2 | `Data`, `string` path | `int` | `int_at document "keep"` |
| `float_at` | 2 | `Data`, `string` path | `float` | `float_at document "rate"` |
| `bool_at` | 2 | `Data`, `string` path | `bool` | `bool_at document "enabled"` |
| `list_at` | 2 | `Data`, `string` path | `list Data` | `list_at document "items"` |

`Data` is a [choice](../statements/choices.md) with the variants `data_null`, `data_bool <bool>`, `data_int <int>`, `data_float <float>`, `data_text <string>`, `data_list <list Data>` and `data_map <map string Data>`. A document read from one format can be written in the other.

## Reading into a record

The usual way to read a file is to declare a record for the parts you need and `convert` to it:

```pyrop
use data library
import from data Data parse_yaml json_text end

record Settings string destination int keep list string folders end

define Data document parse_yaml "destination: backups\nkeep: 3\nfolders:\n  - notes\n  - photos\nignored: true\n"
define Settings settings convert Settings document

write_line the destination of settings
write_line the folders of settings
write_line json_text convert Data settings
```

```output
backups
["notes", "photos"]
{
  "destination": "backups",
  "keep": 3,
  "folders": [
    "notes",
    "photos"
  ]
}
```

- **`convert Data <value>`** works on any value. A record becomes a map of its fields, leaving out an absent `option` field. A choice variant with no data becomes its name, and one with data a single-key map from its name to its data, a list if it carries several values. `none` in a list is `null`.
- **`convert <Type> <Data>`** reads it back to an equal value, except that a `some none` in a nested `option` comes back as `none`, because JSON and YAML have one null. Extra keys are ignored, and a missing `option` field is `none`. A whole number is accepted for a `float`, but not the reverse.
- **A value of the wrong shape** is a runtime error naming where, which [`attempt`](../statements/attempt.md) turns into `none`: `The value at "players.0.name" is int, not string.`
- `convert string <Data>` gives the text form, as `convert string` of anything does. To get the text out of a `data_text`, use `text_at value ""`.

## Reading by path

A path is keys and list positions joined by `.`, and the empty path is the value itself. A key with a `.` in it can only be reached with [`match`](../statements/match.md).

```pyrop
use data library
import from data Data parse_json text_at int_at end

define Data order parse_json "{\"items\": [{\"name\": \"lamp\", \"count\": 2}]}"
write_line text_at order "items.0.name"
write_line int_at order "items.0.count"
if_some int missing attempt int_at order "items.5.count" write_line missing
else write_line "No such item."
```

```output
lamp
2
No such item.
```

## JSON and YAML

- **There are two parsers because almost any text is valid YAML.** `Service unavailable` is a YAML string, so `parse_json` is what tells you that a server sent an error page instead of JSON.
- **`parse_json`** is strict: no comments or trailing commas, and values can nest 1,000 levels deep, past which it is a runtime error, as for `json_text`. A repeated key is an error in both formats.
- **`parse_yaml`** follows YAML 1.2, so `yes`, `no`, `on` and `off` are text. Only the first document is read, keys must be text, custom tags are rejected, and values can nest 1,000 levels deep, past which it is a runtime error. Aliases work, and a document that would expand into more values than a list may hold is an error.
- **`.inf` and `.nan`** cannot be read, since Pyrop floats are always finite.
- **`yaml_text`** quotes any text YAML would misread, such as `yes`, `null` and `123`, and a float keeps its decimal point.
- **`json_text`** is indented, with characters written as themselves, apart from emoji and others outside the Basic Multilingual Plane, which are written as a pair of `\uXXXX` escapes and read back the same.
