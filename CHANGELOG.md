# Changelog

What changed in each release of Pyrop. Versions before 1.0.0 are not listed.

## Unreleased

## 1.0.0 (2026-11-11)

First stable release.

From 1.0 until 2.0, a program that works keeps working:

- No new reserved words. New features arrive as built-in libraries, whose names your program can still use.
- A name keeps its meaning, arity and types, in the core and in every library.
- A library's records and choices keep their fields and variants. New data arrives as a new record or choice.
- Text saved with `convert` by one 1.x version loads in the next.
- A retired name still tells you what replaced it.

A fix may still change behaviour that contradicts the [documentation](docs/index.md). Anything else that would break a program waits for 2.0.
