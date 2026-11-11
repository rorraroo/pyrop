# Built-in libraries: `use … library`

Some built-in operations come in libraries that Pyrop itself provides. A library is loaded with `use` like a file, but with the word `library` in place of the path, and its operations are reached exactly like a module's functions.

```
use <library> library
```

```pyrop
use random library
use math library
import from random roll seed end

seed 7
define int damage roll 1 6
write_line from math round multiply 1.5 convert float damage
```

```output
2
```

| Library | Operations | See |
| --- | --- | --- |
| `files` | `load`, `save`, `append`, `file_exists`, `load_lines`, `files_in`, `files_under`, `folders_in`, `name_of`, `folder_of`, `extension_of`, `join_path`, `delete_file`, `copy_file`, `move_file`, `make_folder`, `delete_folder`, `folder_exists`, `current_folder`, `modified_at` | [Files](../operations/files.md) |
| `program` | `arguments`, `environment`, `run`, `run_attached`, the record `CommandResult` | [Arguments, environment and commands](../operations/program.md) |
| `math` | `absolute`, `sign`, `power`, `square_root`, `round`, `round_down` | [Math](../operations/math.md) |
| `random` | `float_between`, `roll`, `seed` | [Random numbers](../operations/random.md) |
| `terminal` | `View` and its kinds, `show`, `write_view`, `read_key`, `read_key_within` | [Views and keys](../operations/terminal.md) |
| `time` | `now`, `now_utc`, `moment_text`, `parse_moment`, `milliseconds_between`, `add_milliseconds`, `is_before`, `wait`, the record `Moment` | [Dates and waiting](../operations/time.md) |
| `data` | `parse_json`, `parse_yaml`, `json_text`, `yaml_text`, `text_at`, `int_at`, `float_at`, `bool_at`, `list_at`, the choice `Data` | [JSON and YAML](../operations/data.md) |

- **The module name is the library's name**: `use random library`, then `from random roll 1 6`. A misspelt name is an error that lists the libraries.
- **Nothing in a library is a reserved word.** Its names enter your file only through `import`, and only the ones you name, so `round` stays free as a variable name in a file that does not import it.
- **Library operations are built-in operations**, so [`attempt`](attempt.md) takes them.
- `pyrop --build` needs no file for a library.
- **Moved operations.** In versions before 0.16 these operations were part of the core under other names. Using an old name tells you where it went: `'roll_dice' is now 'roll' in the library random: add use random library and import from random roll end, or write from random roll.`

  | Old name | Now |
  | --- | --- |
  | `read_file`, `write_file`, `append_file` | `load`, `save`, `append` in `files` |
  | `file_exists`, `read_lines`, `list_files` | `file_exists`, `load_lines`, `files_in` in `files` |
  | `argument_count`, `argument_at` | `arguments` in `program`: `length_of from program arguments`, `get from program arguments 0` |
  | `read_environment` | `environment` in `program` |
  | `abs`, `to_power`, `square_root`, `round`, `round_down` | `absolute`, `power`, `square_root`, `round`, `round_down` in `math` |
  | `random`, `roll_dice`, `seed_random` | `float_between`, `roll`, `seed` in `random` |

- **Renamed operations.** Version 0.30 gave six library operations clearer names, and the old names say where they went: `exists` is now `file_exists`, `listing` is `files_in` and `listing_all` is `files_under` in `files`, `abs` is `absolute` in `math`, `between` is `float_between` in `random`, and `plus_milliseconds` is `add_milliseconds` in `time`.
