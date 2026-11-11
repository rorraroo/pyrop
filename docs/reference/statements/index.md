# Statements

A program is a sequence of statements. Each statement starts with a keyword or a call, and its parts follow in a fixed order, so no brackets or separators are needed.

**Variables and output**

- [`define`](define.md): declare a variable
- [`set`](set.md): change a variable
- [`write` and `write_line`](write.md): print to standard output
- [`write_error` and `write_error_line`](write-error.md): print to standard error
- [`styled`](styled.md): give output a style

**Control flow**

- [`if` and `else`](if.md)
- [`if_some` and `else`](if-some.md): use an optional value when it is there
- [`while`](while.md)
- [`for`](for.md)
- [`break` and `continue`](break-and-continue.md)
- [`block` … `end`](block.md): several statements where one is expected
- [`exit`](exit.md): end the program with an exit code

**Functions**

- [`function`](function.md)
- [`return`](return.md)
- [Calls as statements](calls-as-statements.md)

**Types of your own**

- [Records: `record`, `the … of` and `copy … with`](records.md)
- [Choices: `choice`](choices.md)
- [Handling every variant: `match` and `when`](match.md)
- [Optional values: `option`, `some`, `none` and `value_or`](options.md)
- [Recoverable errors: `attempt`](attempt.md)

**Programs in several files**

- [Modules: `use`, `from`, `import` and `export`](modules.md)
- [Built-in libraries: `use … library`](libraries.md)
