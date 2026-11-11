# Files: the library `files`

These come from the [library](../statements/libraries.md) `files`: add `use files library`. A program without it cannot touch files.

| Operation | Arity | Arguments | Result | Example |
| --- | --- | --- | --- | --- |
| `load` | 1 | `string` path | `string` | `load "notes.txt"` → the whole file |
| `save` | 2 | `string` path, `string` text | none | `save "out.txt" "done\n"` |
| `append` | 2 | `string` path, `string` text | none | `append "log.txt" "started\n"` |
| `file_exists` | 1 | `string` path | `bool` | `file_exists "notes.txt"` → `true` |
| `load_lines` | 1 | `string` path | `list string` | `load_lines "todo.txt"` → `["milk", "bread"]` |
| `files_in` | 1 | `string` directory path | `list string` | `files_in "saves"` → `["one.save", "two.save"]` |
| `name_of` | 1 | `string` path | `string` | `name_of "logs/server.log"` → `"server.log"` |
| `folder_of` | 1 | `string` path | `string` | `folder_of "logs/server.log"` → `"logs"` |
| `extension_of` | 1 | `string` path | `string` | `extension_of "a.tar.gz"` → `".gz"` |
| `join_path` | 2 | `string` first, `string` second | `string` | `join_path "logs" "a.log"` → `"logs/a.log"` |
| `delete_file` | 1 | `string` path | none | `delete_file "old.txt"` |
| `copy_file` | 2 | `string` from, `string` to | none | `copy_file "a.png" "b.png"` |
| `move_file` | 2 | `string` from, `string` to | none | `move_file "a.txt" "done/a.txt"` |
| `make_folder` | 1 | `string` path | none | `make_folder "backups/old"` |
| `delete_folder` | 1 | `string` path | none | `delete_folder "backups/old"` |
| `folder_exists` | 1 | `string` path | `bool` | `folder_exists "backups"` → `true` |
| `folders_in` | 1 | `string` folder path | `list string` | `folders_in "backups"` → `["monday"]` |
| `files_under` | 1 | `string` folder path | `list string` | `files_under "src"` → `["a.txt", "sub/b.txt"]` |
| `current_folder` | 0 | none | `string` | `current_folder` → the full path |
| `modified_at` | 1 | `string` path | `Moment` | `modified_at "a.txt"` → when it last changed |

```pyrop
use files library
import from files file_exists load save load_lines end

save "todo.txt" "- milk\nnotes\n- bread\n"
save "copy.txt" load "todo.txt"
if file_exists "copy.txt" write_line "Copied."
for string line in load_lines "todo.txt" if starts_with line "- " write_line line
```

```output
Copied.
- milk
- bread
```

- **`save`** replaces the file's content, or creates the file. **`append`** adds to the end, and also creates the file if needed.
- `save` and `append` give no value, so they can only be used as statements, like calls to `void` functions.
- **Relative paths** start from the directory you run `pyrop` in, not from the script's directory.
- **Files are UTF-8 text.** Line endings are read as `\n`, whatever the file uses. Reading a file that is not UTF-8 text, such as an image, is a runtime error.
- **`file_exists`** is `false` for directories and never fails. `load`, `load_lines`, `save` and `append` fail with a runtime error when the file or its directory is missing, the path is invalid or a directory, or access is denied. Directories are never created for you. Use [`attempt`](../statements/attempt.md) to handle these failures yourself.
- **`load_lines`** gives the lines without their line breaks. A final line break does not add an empty line at the end; empty lines in the middle are kept, so the line number is the position in the list plus one.
- **`files_in`** gives the names of the files in a directory, without the directory part and without subdirectories, sorted by character code (so `B` comes before `a`). Hidden files are included. A missing directory, or a path that is a file, is a runtime error.
- **`name_of`** gives the file name of a path: the text after the last `/` or `\`, on every system. It only looks at the text, never at the disk, so it never fails; a path ending in a separator gives `""`.

```pyrop
use files library

write_line from files name_of "logs/server.log"
write_line from files name_of "C:\\Users\\ann\\notes.txt"
write_line from files name_of "readme.md"
```

```output
server.log
notes.txt
readme.md
```

- **`delete_file`** removes one file, and **`delete_folder`** removes a folder and everything in it. `delete_folder` refuses a root folder and any folder the program is running inside, and a path written as a device or a share on this computer. Links inside are removed, not followed. A missing file or folder is a runtime error.
- **`copy_file`** copies any file byte for byte, so it works on images too, and keeps the modification time. **`move_file`** moves a file. Both replace an existing destination and neither creates folders; a file cannot be copied or moved onto itself.
- **`make_folder`** creates a folder and any missing parents, and does nothing if it is already there.
- **`join_path`** joins two paths with `/`, which every system accepts, so a program prints the same path everywhere. A second part that is a full path, such as `/etc` or `C:\x`, is a runtime error.
- **`folder_of`** gives the text before the last `/` or `\`, keeping a root whole, so `folder_of "/a"` is `"/"` and `folder_of "C:\a"` is `"C:\"`; and **`extension_of`** the part of the file name from its last `.`, including the dot. A name with no dot, or whose only dot comes first, such as `.bashrc`, gives `""`. Like `name_of`, these look only at the text and never fail.
- **`files_under`** gives every file in a folder and its subfolders, as paths relative to it joined with `/`, sorted by character code. **`folders_in`** gives the names of a folder's direct subfolders, sorted the same way.
- **`modified_at`** gives a [`Moment`](time.md) in local time.

```pyrop
use files library
import from files save make_folder copy_file delete_file delete_folder join_path folder_of extension_of files_under folders_in folder_exists file_exists end

make_folder "work/notes"
save "work/notes/a.txt" "first"
copy_file "work/notes/a.txt" join_path "work" "b.txt"
write_line files_under "work"
write_line folders_in "work"
write_line folder_of "work/notes/a.txt"
write_line extension_of "work/notes/a.txt"

delete_file "work/b.txt"
write_line file_exists "work/b.txt"
delete_folder "work"
write_line folder_exists "work"
```

```output
["b.txt", "notes/a.txt"]
["notes"]
work/notes
.txt
false
false
```

## A log and a list of saves

`append` adds lines to a log, and `files_in` finds the save files in the current directory:

```pyrop
use files library
import from files save append load load_lines file_exists files_in end

save "log.txt" "started\n"
append "log.txt" "loaded the map\n"
append "log.txt" "saved\n"
write load "log.txt"
write_line fill "The log has {} lines." list_of int length_of load_lines "log.txt" end

save "one.save" "level 1"
save "two.save" "level 4"
for string name in files_in "." if ends_with name ".save" write_line name
if not file_exists "three.save" write_line "There is no third save."
```

```output
started
loaded the map
saved
The log has 3 lines.
one.save
two.save
There is no third save.
```

## When a file is missing

Without `attempt`, a missing file stops the program:

```pyrop
use files library

write_line from files load "missing.txt"
```

```error
File "missing.txt" does not exist.
```

With `attempt`, the program decides what to do instead:

```pyrop
use files library
import from files load end

if_some string text attempt load "settings.txt" write_line text
else write_line "No settings yet; using the defaults."
```

```output
No settings yet; using the defaults.
```
