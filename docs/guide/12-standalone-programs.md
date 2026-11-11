# 12. Building a standalone program

A finished program can be turned into a single executable file, which runs on a computer that does not have Pyrop installed:

```
pyrop --build report.prp
```

This makes `report.exe` on Windows, or `report` elsewhere, next to the script. Choose another name with `-o`:

```
pyrop --build report.prp -o tools/report
```

- **Everything is checked first.** A program with a parse or type error is not built, so a built program never fails on a typo.
- **Its modules go with it.** Every file the program uses is packed into the executable, so it can be copied anywhere. Built-in libraries need no file.
- **Arguments and input work as before:** `report data.txt` gives the program `data.txt`, and its input and output can be piped like any command's.
- The executable is built for the computer you build it on.

## Plain output

A program's output is plain, without colours or interactive menus, when it goes to a file or a pipe, when the environment variable `NO_COLOR` is set, or when you ask for it:

```
pyrop --plain report.prp
```

Plain output is what screen readers, logs and other programs read best, and Pyrop programs are written so that nothing is lost in it.

## Where to go next

- [Two finished programs](13-two-finished-programs.md) reads the dashboard and the game that come with Pyrop.
- The [reference](../reference/index.md) describes every statement and operation in full.
- The sample programs that come with Pyrop show complete programs: `logcheck`, a log checker; `adventure`, a text adventure in five files; `rogue`, a small roguelite with saved progress; `logwatch`, a live dashboard; `gitsummary`, which runs `git` and reports on its output; and `backup`, which reads its settings from YAML, copies changed files into a dated folder, removes old backups and writes a JSON report.
