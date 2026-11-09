using static Pyrop.Runtime.Builtins;
using Pyrop.Parsing;

namespace Pyrop.Runtime;

public static class LibraryOperations
{
    private static readonly PyropType I = PyropType.Int;
    private static readonly PyropType F = PyropType.Float;
    private static readonly PyropType S = PyropType.String;
    private static readonly PyropType B = PyropType.Bool;
    private static readonly PyropType V = PyropType.Void;
    private static readonly PyropType Strings = PyropTypes.ListOf(PyropType.String);

    private static RecordType DeclareCommandResult()
    {
        var position = new SourcePosition(0, 0);
        var record = new RecordType("CommandResult", ProgramModule, position, isExported: true);
        record.Define([new RecordField(I, "exit_code", position), new RecordField(S, "output", position), new RecordField(S, "errors", position)]);
        return record;
    }

    private static (string Command, List<string> Arguments) CommandOf(IBuiltinInvocation invocation)
    {
        var command = invocation.Argument(0).AsString();
        var arguments = invocation.Argument(1).AsList().Items.Select(argument => argument.AsString()).ToList();
        if (command.Length == 0)
        {
            throw invocation.Fail("A command needs a program name; it was empty.");
        }

        if (command.Contains('"') || command != command.Trim())
        {
            throw invocation.Fail("A program name cannot contain a quotation mark or start or end with a space; its arguments go in the list.");
        }

        if (arguments.Prepend(command).Any(part => part.Contains('\0')))
        {
            throw invocation.Fail($"A command cannot contain a NUL character: \"{command}\".");
        }

        return (command, arguments);
    }

    private static Value RunCaptured(IBuiltinInvocation invocation)
    {
        var (command, arguments) = CommandOf(invocation);
        try
        {
            var outcome = invocation.Host.Commands.Capture(command, arguments);
            return new RecordValue(CommandResult, [new IntValue(outcome.ExitCode), new StringValue(outcome.Output), new StringValue(outcome.Errors)]);
        }
        catch (CommandFailedException failure)
        {
            throw invocation.Fail(failure.Message);
        }
    }

    private static Value RunAttached(IBuiltinInvocation invocation)
    {
        var (command, arguments) = CommandOf(invocation);
        invocation.Output.BeforeAttachedCommand();
        invocation.ShownScreen.Panels = null;
        try
        {
            return new IntValue(invocation.Host.Commands.RunAttached(command, arguments));
        }
        catch (CommandFailedException failure)
        {
            throw invocation.Fail(failure.Message);
        }
    }

    private static readonly Module DataModule = BuiltinLibrary.ModuleFor("data");

    public static ChoiceType DataChoice { get; } = DataOperations.Declare(DataModule);

    public static BuiltinLibrary Data { get; } = new("data",
    [
        Operation("parse_json",
            Overload([S], DataChoice, c => DataOperations.ParseJson(c, c.Argument(0).AsString()))),
        Operation("parse_yaml",
            Overload([S], DataChoice, c => DataOperations.ParseYaml(c, c.Argument(0).AsString()))),
        Operation("json_text",
            Overload([DataChoice], S, c => new StringValue(DataOperations.JsonText(c, c.Argument(0))))),
        Operation("yaml_text",
            Overload([DataChoice], S, c => new StringValue(DataOperations.YamlText(c, c.Argument(0))))),
        Operation("text_at",
            Overload([DataChoice, S], S, c => DataOperations.TextAt(c, c.Argument(0), c.Argument(1).AsString()))),
        Operation("int_at",
            Overload([DataChoice, S], I, c => DataOperations.IntAt(c, c.Argument(0), c.Argument(1).AsString()))),
        Operation("float_at",
            Overload([DataChoice, S], F, c => DataOperations.FloatAt(c, c.Argument(0), c.Argument(1).AsString()))),
        Operation("bool_at",
            Overload([DataChoice, S], B, c => DataOperations.BoolAt(c, c.Argument(0), c.Argument(1).AsString()))),
        Operation("list_at",
            Overload([DataChoice, S], PyropTypes.ListOf(DataChoice), c => DataOperations.ListAt(c, c.Argument(0), c.Argument(1).AsString()))),
    ], new Dictionary<string, string>(), DataModule, [DataChoice]);

    private static readonly Module TimeModule = BuiltinLibrary.ModuleFor("time");

    public static RecordType Moment { get; } = TimeOperations.DeclareMoment(TimeModule);

    public static BuiltinLibrary Time { get; } = new("time",
    [
        Operation("now",
            Overload([], Moment, c => TimeOperations.MomentOf(c.Host.Clock.Now))),
        Operation("now_utc",
            Overload([], Moment, c => TimeOperations.MomentOf(c.Host.Clock.UtcNow))),
        Operation("moment_text",
            Overload([Moment, S], S, c => new StringValue(TimeOperations.Text(c, c.Argument(0), c.Argument(1).AsString())))),
        Operation("parse_moment",
            Overload([S, S], Moment, c => TimeOperations.Parse(c, c.Argument(0).AsString(), c.Argument(1).AsString()))),
        Operation("milliseconds_between",
            Overload([Moment, Moment], I, c => new IntValue(TimeOperations.MillisecondsBetween(c, c.Argument(0), c.Argument(1))))),
        Operation("plus_milliseconds",
            Overload([Moment, I], Moment, c => TimeOperations.PlusMilliseconds(c, c.Argument(0), c.Argument(1).AsInt()))),
        Operation("is_before",
            Overload([Moment, Moment], B, c => BoolValue.Of(TimeOperations.IsBefore(c, c.Argument(0), c.Argument(1))))),
        Operation("wait",
            Overload([I], V, c => TimeOperations.Wait(c, c.Argument(0).AsInt()))),
    ], new Dictionary<string, string>(), TimeModule, records: [Moment]);

    public static BuiltinLibrary Files { get; } = new("files",
    [
        Operation("load",
            Overload([S], S, c => new StringValue(TextFiles.Read(c, c.Argument(0).AsString())))),
        Operation("save",
            Overload([S, S], V, c => TextFiles.Write(c, c.Argument(0).AsString(), c.Argument(1).AsString(), append: false))),
        Operation("append",
            Overload([S, S], V, c => TextFiles.Write(c, c.Argument(0).AsString(), c.Argument(1).AsString(), append: true))),
        Operation("exists",
            Overload([S], B, c => BoolValue.Of(File.Exists(c.Argument(0).AsString())))),
        Operation("load_lines",
            Overload([S], Strings, c => TextFiles.ReadLines(c, c.Argument(0).AsString()))),
        Operation("listing",
            Overload([S], Strings, c => TextFiles.ListFiles(c, c.Argument(0).AsString()))),
        Operation("name_of",
            Overload([S], S, c => new StringValue(FileOperations.NameOf(c.Argument(0).AsString())))),
        Operation("delete_file",
            Overload([S], V, c => FileOperations.DeleteFile(c, c.Argument(0).AsString()))),
        Operation("copy_file",
            Overload([S, S], V, c => FileOperations.CopyFile(c, c.Argument(0).AsString(), c.Argument(1).AsString()))),
        Operation("move_file",
            Overload([S, S], V, c => FileOperations.MoveFile(c, c.Argument(0).AsString(), c.Argument(1).AsString()))),
        Operation("make_folder",
            Overload([S], V, c => FileOperations.MakeFolder(c, c.Argument(0).AsString()))),
        Operation("delete_folder",
            Overload([S], V, c => FileOperations.DeleteFolder(c, c.Argument(0).AsString()))),
        Operation("join_path",
            Overload([S, S], S, c => new StringValue(FileOperations.JoinPath(c, c.Argument(0).AsString(), c.Argument(1).AsString())))),
        Operation("folder_of",
            Overload([S], S, c => new StringValue(FileOperations.FolderOf(c.Argument(0).AsString())))),
        Operation("extension_of",
            Overload([S], S, c => new StringValue(FileOperations.ExtensionOf(c.Argument(0).AsString())))),
        Operation("listing_all",
            Overload([S], Strings, c => TextFiles.ListAllFiles(c, c.Argument(0).AsString()))),
        Operation("current_folder",
            Overload([], S, c => new StringValue(Environment.CurrentDirectory))),
        Operation("folder_exists",
            Overload([S], B, c => BoolValue.Of(Directory.Exists(c.Argument(0).AsString())))),
        Operation("folders_in",
            Overload([S], Strings, c => TextFiles.ListFolders(c, c.Argument(0).AsString()))),
        Operation("modified_at",
            Overload([S], Moment, c => FileOperations.ModifiedAt(c, c.Argument(0).AsString()))),
    ], new Dictionary<string, string>
    {
        ["read_file"] = "load",
        ["write_file"] = "save",
        ["append_file"] = "append",
        ["file_exists"] = "exists",
        ["read_lines"] = "load_lines",
        ["list_files"] = "listing",
    });

    private static readonly Module ProgramModule = BuiltinLibrary.ModuleFor("program");

    public static RecordType CommandResult { get; } = DeclareCommandResult();

    public static BuiltinLibrary Program { get; } = new("program",
    [
        Operation("arguments",
            Overload([], Strings, c => new ListValue(Strings, [.. c.Host.Arguments.Select(argument => (Value)new StringValue(argument))]))),
        Operation("environment",
            Overload([S], S, c => new StringValue(ReadEnvironment(c, c.Argument(0).AsString())))),
        Operation("run",
            Overload([S, Strings], CommandResult, RunCaptured)),
        Operation("run_attached",
            Overload([S, Strings], I, RunAttached)),
    ], new Dictionary<string, string>
    {
        ["argument_count"] = "arguments",
        ["argument_at"] = "arguments",
        ["read_environment"] = "environment",
    }, ProgramModule, records: [CommandResult]);

    public static BuiltinLibrary Math { get; } = new("math",
    [
        Operation("abs",
            Overload([I], I, c => Int(c, () => System.Math.Abs(c.Argument(0).AsInt()))),
            Overload([F], F, c => new FloatValue(System.Math.Abs(c.Argument(0).AsFloat())))),
        Operation("sign",
            Overload([I], I, c => new IntValue(System.Math.Sign(c.Argument(0).AsInt()))),
            Overload([F], F, c => new FloatValue(System.Math.Sign(c.Argument(0).AsFloat())))),
        Operation("power",
            Overload([I, I], I, c => Power(c, c.Argument(0).AsInt(), c.Argument(1).AsInt())),
            Overload([F, F], F, c => Float(c, System.Math.Pow(c.Argument(0).AsFloat(), c.Argument(1).AsFloat())))),
        Operation("square_root",
            Overload([F], F, c => new FloatValue(System.Math.Sqrt(NonNegative(c, c.Argument(0).AsFloat()))))),
        Operation("round",
            Overload([F], I, c => Conversions.FloatToInt(System.Math.Round(c.Argument(0).AsFloat(), MidpointRounding.AwayFromZero), c.Fail))),
        Operation("round_down",
            Overload([F], I, c => Conversions.FloatToInt(System.Math.Floor(c.Argument(0).AsFloat()), c.Fail))),
    ], new Dictionary<string, string>
    {
        ["to_power"] = "power",
    });

    public static BuiltinLibrary Random { get; } = new("random",
    [
        Operation("between",
            Overload([F, F], F, c => new FloatValue(Between(c, c.Argument(0).AsFloat(), c.Argument(1).AsFloat())))),
        Operation("roll",
            Overload([I, I], I, c => new IntValue(Roll(c, c.Argument(0).AsInt(), c.Argument(1).AsInt())))),
        Operation("seed",
            Overload([I], V, c => Seed(c, c.Argument(0).AsInt()))),
    ], new Dictionary<string, string>
    {
        ["random"] = "between",
        ["roll_dice"] = "roll",
        ["seed_random"] = "seed",
    });

    private static readonly Module TerminalModule = BuiltinLibrary.ModuleFor("terminal");

    public static ChoiceType TerminalView { get; } = Views.Declare(TerminalModule);

    public static BuiltinLibrary Terminal { get; } = new("terminal",
    [
        Operation("show",
            Overload([TerminalView], V, c => Views.Show(c, c.Argument(0)))),
        Operation("write_view",
            Overload([TerminalView], V, c => Views.WriteView(c, c.Argument(0)))),
        Operation("read_key",
            Overload([], S, c => new StringValue(Keys.Read(c)))),
        Operation("read_key_within",
            Overload([I], PyropTypes.OptionOf(S), c => Keys.ReadWithin(c, c.Argument(0).AsInt()))),
    ], new Dictionary<string, string>(), TerminalModule, [TerminalView]);

    private static IntValue Power(IBuiltinInvocation invocation, long number, long exponent)
    {
        if (exponent < 0)
        {
            throw invocation.Fail($"'power' on ints cannot take a negative exponent ({exponent}); use floats instead.");
        }

        return Int(invocation, () =>
        {
            var result = 1L;
            while (true)
            {
                if ((exponent & 1) == 1)
                {
                    result = checked(result * number);
                }

                exponent >>= 1;
                if (exponent == 0)
                {
                    return result;
                }

                number = checked(number * number);
            }
        });
    }

    private static double NonNegative(IBuiltinInvocation invocation, double number) =>
        number >= 0 ? number : throw invocation.Fail($"Cannot take the square root of a negative number ({new FloatValue(number).ToText()}).");

    private static double Between(IBuiltinInvocation invocation, double lower, double upper)
    {
        if (!(lower < upper))
        {
            throw invocation.Fail(
                $"'between' needs a lower bound below its upper bound, found {new FloatValue(lower).ToText()} and {new FloatValue(upper).ToText()}.");
        }

        var fraction = invocation.Random.NextFraction();
        var result = lower * (1 - fraction) + upper * fraction;
        return result < upper ? System.Math.Max(result, lower) : System.Math.BitDecrement(upper);
    }

    private static long Roll(IBuiltinInvocation invocation, long lower, long upper) =>
        lower <= upper
            ? invocation.Random.NextInRange(lower, upper)
            : throw invocation.Fail($"'roll' needs a lower bound no greater than its upper bound, found {lower} and {upper}.");

    private static Value? Seed(IBuiltinInvocation invocation, long seed)
    {
        invocation.Random.Seed(seed);
        return null;
    }

    private static string ReadEnvironment(IBuiltinInvocation invocation, string name)
    {
        if (name.Length == 0)
        {
            throw invocation.Fail("An environment variable name cannot be empty.");
        }

        if (name.Any(character => character == '=' || char.IsControl(character)))
        {
            throw invocation.Fail($"\"{name}\" is not a valid environment variable name; it cannot contain '=' or control characters.");
        }

        return invocation.Host.ReadEnvironment(name)
            ?? (invocation.IsAttempted ? throw invocation.Fail($"Environment variable \"{name}\" is not set.") : "");
    }
}
