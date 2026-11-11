# Guide

The guide teaches Pyrop in the order a program needs it. It starts with reading code, because code without brackets is easy to write and only takes a little practice to read. Each chapter builds on the ones before it. The [reference](../reference/index.md) has the full details of everything the guide introduces.

1. [Reading Pyrop](01-reading-pyrop.md): how each operation takes its arguments, and how to see where an expression ends
2. [Values, variables and output](02-values-and-variables.md)
3. [Making decisions and repeating](03-decisions-and-loops.md)
4. [Functions](04-functions.md)
5. [Lists and maps](05-lists-and-maps.md)
6. [Records](06-records.md)
7. [Choices and `match`](07-choices-and-match.md)
8. [When something can go wrong: options and `attempt`](08-options-and-attempt.md)
9. [Programs in several files: modules and built-in libraries](09-modules-and-libraries.md)
10. [Talking to the user: prompts, menus and tables](10-talking-to-the-user.md)
11. [Full-screen programs: views and keys](11-views-and-keys.md)
12. [Building a standalone program](12-standalone-programs.md)
13. [Two finished programs](13-two-finished-programs.md): how a dashboard and a small game put it all together

## How to read the examples

Every example is a complete program. After it comes what it writes, and sometimes what it reads or the error it causes:

- **Output** is exactly what the program writes, as it appears when its output goes to a file, with error messages it writes itself in the same place, as on a terminal. Colours and interactive menus are left out.
- **Input** is what the user types, one line per answer. It is given before the program runs, so it does not appear in the output.
- **Error** is the message Pyrop shows when it stops the program.

You can copy any example into a file and run it with `pyrop`, and every one is run as a test against the current version of Pyrop, so what you read here is what Pyrop does.
