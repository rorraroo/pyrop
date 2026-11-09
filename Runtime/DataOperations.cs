using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Pyrop.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Pyrop.Runtime;

public static class DataOperations
{
    private const int Null = 0;
    private const int Bool = 1;
    private const int Int = 2;
    private const int Float = 3;
    private const int Text = 4;
    private const int List = 5;
    private const int Map = 6;

    private const string CoreTagPrefix = "tag:yaml.org,2002:";
    private const int MostYamlNesting = 1000;

    private static readonly string[] KindNames = ["null", "bool", "int", "float", "text", "list", "map"];

    private static readonly HashSet<string> CoreTags = ["str", "int", "float", "bool", "null", "map", "seq"];

    private static readonly HashSet<string> WordsYamlMisreads =
        new(["y", "n", "yes", "no", "on", "off", "true", "false", "null", "~"], StringComparer.OrdinalIgnoreCase);

    public static ChoiceType Declare(Module module)
    {
        var position = new SourcePosition(0, 0);
        var data = new ChoiceType("Data", module, position, isExported: true,
        [
            new("data_null", position, 0),
            new("data_bool", position, 1),
            new("data_int", position, 1),
            new("data_float", position, 1),
            new("data_text", position, 1),
            new("data_list", position, 1),
            new("data_map", position, 1),
        ]);
        PyropType[][] carried =
        [
            [],
            [PyropType.Bool],
            [PyropType.Int],
            [PyropType.Float],
            [PyropType.String],
            [PyropTypes.ListOf(data)],
            [PyropTypes.MapOf(PyropType.String, data)],
        ];
        foreach (var (variant, types) in data.Variants.Zip(carried))
        {
            variant.Define(types);
        }

        return data;
    }

    private static ChoiceType DataType => LibraryOperations.DataChoice;

    private static PyropType ListType { get; } = PyropTypes.ListOf(LibraryOperations.DataChoice);

    private static PyropType MapType { get; } = PyropTypes.MapOf(PyropType.String, LibraryOperations.DataChoice);

    private static ChoiceValue NullValue => DataType.ValueOf(Null);

    private static ChoiceValue Carrying(int variant, Value content) => new(DataType, variant, [content]);

    private static ChoiceValue OfList(ImmutableList<Value> items) => Carrying(List, new ListValue(ListType, items));

    private static ChoiceValue OfMap(MapValue map) => Carrying(Map, map);

    public static Value ParseJson(IBuiltinInvocation invocation, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = int.MaxValue });
            return FromJson(invocation, document.RootElement, 0);
        }
        catch (JsonException exception)
        {
            throw invocation.Fail($"The text is not valid JSON (line {(exception.LineNumber ?? 0) + 1}).");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw invocation.Fail("The text is not valid JSON; it contains characters JSON cannot hold.");
        }
    }

    public static Value ParseYaml(IBuiltinInvocation invocation, string text)
    {
        try
        {
            return new YamlReader(invocation, text).Read();
        }
        catch (YamlException exception)
        {
            throw invocation.Fail($"The text is not valid YAML (line {exception.Start.Line}).");
        }
        catch (InvalidOperationException)
        {
            throw invocation.Fail("The YAML text ends before it is complete.");
        }
        catch (ArgumentException)
        {
            throw invocation.Fail("The text is not valid YAML; it holds an escape that is no character.");
        }
    }

    public static string JsonText(IBuiltinInvocation invocation, Value data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                Indented = true,
                NewLine = "\n",
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                SkipValidation = true,
                MaxDepth = invocation.NestingLimit + 1,
            }))
            {
                WriteJson(invocation, writer, data, 0);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw invocation.Fail("This data holds characters that cannot be written as JSON.");
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string YamlText(IBuiltinInvocation invocation, Value data)
    {
        var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var emitter = new Emitter(text, EmitterSettings.Default.WithBestWidth(int.MaxValue).WithIndentedSequences().WithNewLine("\n"));
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        WriteYaml(invocation, emitter, data, 0);
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());
        return text.ToString();
    }

    public static Value At(IBuiltinInvocation invocation, Value root, string path)
    {
        var current = (ChoiceValue)root;
        if (path.Length == 0)
        {
            return current;
        }

        foreach (var part in path.Split('.'))
        {
            switch (current.Index)
            {
                case Map:
                    current = (ChoiceValue)(current.Data[0].AsMap().Find(new StringValue(part))
                        ?? throw invocation.Fail($"Nothing is at \"{path}\": there is no key \"{part}\"."));
                    break;
                case List:
                    var items = current.Data[0].AsList();
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= items.Count)
                    {
                        throw invocation.Fail($"Nothing is at \"{path}\": the list has no position \"{part}\".");
                    }

                    current = (ChoiceValue)items.Items[index];
                    break;
                default:
                    throw invocation.Fail($"Nothing is at \"{path}\": \"{part}\" is inside a {KindNames[current.Index]}, which holds no parts.");
            }
        }

        return current;
    }

    public static Value TextAt(IBuiltinInvocation invocation, Value root, string path) => Expect(invocation, root, path, Text);

    public static Value IntAt(IBuiltinInvocation invocation, Value root, string path) => Expect(invocation, root, path, Int);

    public static Value BoolAt(IBuiltinInvocation invocation, Value root, string path) => Expect(invocation, root, path, Bool);

    public static Value ListAt(IBuiltinInvocation invocation, Value root, string path) => Expect(invocation, root, path, List);

    public static Value FloatAt(IBuiltinInvocation invocation, Value root, string path)
    {
        var found = (ChoiceValue)At(invocation, root, path);
        return found.Index == Int
            ? new FloatValue(found.Data[0].AsInt())
            : Content(invocation, found, path, Float);
    }

    public static bool Involves(PyropType from, PyropType to) => to == DataType || from == DataType;

    public static Value FromValue(Value value, Func<string, Exception> tooDeep, int nestingLimit) =>
        FromValue(value, tooDeep, nestingLimit, 0);

    public static Value ToValue(Value data, PyropType target, Func<string, Exception> fail, Func<string, Exception> tooDeep, int nestingLimit) =>
        new Reading(fail, tooDeep, nestingLimit).As((ChoiceValue)data, target, "", 0);

    private static Value Expect(IBuiltinInvocation invocation, Value root, string path, int variant) =>
        Content(invocation, (ChoiceValue)At(invocation, root, path), path, variant);

    private static Value Content(IBuiltinInvocation invocation, ChoiceValue found, string path, int variant) =>
        found.Index == variant
            ? found.Data[0]
            : throw invocation.Fail($"{Where(path)} is {KindNames[found.Index]}, not {KindNames[variant]}.");

    private static string Where(string path) => path.Length == 0 ? "The value" : $"The value at \"{path}\"";

    private static void GuardYamlDepth(IBuiltinInvocation invocation, int depth)
    {
        if (depth >= MostYamlNesting)
        {
            throw invocation.Fail($"YAML can nest at most {MostYamlNesting.ToString("N0", CultureInfo.InvariantCulture)} levels deep.");
        }

        GuardDepth(invocation, depth);
    }

    private static void GuardDepth(IBuiltinInvocation invocation, int depth)
    {
        if (depth >= invocation.NestingLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw invocation.FailAtLimit("This data is nested too deeply.");
        }
    }

    private static Value FromJson(IBuiltinInvocation invocation, JsonElement element, int depth)
    {
        GuardDepth(invocation, depth);
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return NullValue;
            case JsonValueKind.True:
                return Carrying(Bool, BoolValue.True);
            case JsonValueKind.False:
                return Carrying(Bool, BoolValue.False);
            case JsonValueKind.String:
                return Carrying(Text, new StringValue(element.GetString()!));
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var whole))
                {
                    return Carrying(Int, new IntValue(whole));
                }

                return element.TryGetDouble(out var number) && double.IsFinite(number)
                    ? Carrying(Float, new FloatValue(number))
                    : throw invocation.Fail($"The number {element.GetRawText()} is too large to read.");
            case JsonValueKind.Array:
                var items = ImmutableList.CreateBuilder<Value>();
                foreach (var item in element.EnumerateArray())
                {
                    CollectionOperations.CheckRoom(items.Count + 1, invocation.CollectionLimit, "list", invocation.Fail);
                    items.Add(FromJson(invocation, item, depth + 1));
                }

                return OfList(items.ToImmutable());
            default:
                var map = MapValue.Empty(MapType);
                foreach (var property in element.EnumerateObject())
                {
                    var key = new StringValue(property.Name);
                    if (map.ContainsKey(key))
                    {
                        throw invocation.Fail($"The JSON text repeats the key \"{property.Name}\".");
                    }

                    CollectionOperations.CheckRoom(map.Count + 1, invocation.CollectionLimit, "map", invocation.Fail);
                    map = map.Put(key, FromJson(invocation, property.Value, depth + 1));
                }

                return OfMap(map);
        }
    }

    private static void WriteJson(IBuiltinInvocation invocation, Utf8JsonWriter writer, Value data, int depth)
    {
        GuardDepth(invocation, depth);
        var value = (ChoiceValue)data;
        switch (value.Index)
        {
            case Null:
                writer.WriteNullValue();
                break;
            case Bool:
                writer.WriteBooleanValue(value.Data[0].AsBool());
                break;
            case Int:
                writer.WriteNumberValue(value.Data[0].AsInt());
                break;
            case Float:
                writer.WriteRawValue(value.Data[0].ToText(), skipInputValidation: true);
                break;
            case Text:
                writer.WriteStringValue(value.Data[0].AsString());
                break;
            case List:
                writer.WriteStartArray();
                foreach (var item in value.Data[0].AsList().Items)
                {
                    WriteJson(invocation, writer, item, depth + 1);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStartObject();
                foreach (var (key, stored) in value.Data[0].AsMap().Entries)
                {
                    writer.WritePropertyName(key.AsString());
                    WriteJson(invocation, writer, stored, depth + 1);
                }

                writer.WriteEndObject();
                break;
        }
    }

    private static void WriteYaml(IBuiltinInvocation invocation, Emitter emitter, Value data, int depth)
    {
        GuardYamlDepth(invocation, depth);
        var value = (ChoiceValue)data;
        switch (value.Index)
        {
            case Null:
                emitter.Emit(PlainScalar("null"));
                break;
            case Bool:
                emitter.Emit(PlainScalar(value.Data[0].AsBool() ? "true" : "false"));
                break;
            case Int:
            case Float:
                emitter.Emit(PlainScalar(value.Data[0].ToText()));
                break;
            case Text:
                emitter.Emit(TextScalar(value.Data[0].AsString()));
                break;
            case List:
                var items = value.Data[0].AsList().Items;
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, items.IsEmpty ? SequenceStyle.Flow : SequenceStyle.Block));
                foreach (var item in items)
                {
                    WriteYaml(invocation, emitter, item, depth + 1);
                }

                emitter.Emit(new SequenceEnd());
                break;
            default:
                var map = value.Data[0].AsMap();
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, map.Count == 0 ? MappingStyle.Flow : MappingStyle.Block));
                foreach (var (key, stored) in map.Entries)
                {
                    emitter.Emit(TextScalar(key.AsString()));
                    WriteYaml(invocation, emitter, stored, depth + 1);
                }

                emitter.Emit(new MappingEnd());
                break;
        }
    }

    private static Scalar PlainScalar(string text) =>
        new(AnchorName.Empty, TagName.Empty, text, ScalarStyle.Plain, isPlainImplicit: true, isQuotedImplicit: false);

    private static Scalar TextScalar(string text) =>
        YamlWouldMisread(text)
            ? new Scalar(AnchorName.Empty, TagName.Empty, text, ScalarStyle.DoubleQuoted, isPlainImplicit: false, isQuotedImplicit: true)
            : PlainScalar(text);

    private static bool YamlWouldMisread(string text) =>
        text.Length == 0
        || WordsYamlMisreads.Contains(text)
        || char.IsAsciiDigit(text[0])
        || text[0] is '+' or '-' or '.'
        || text.Any(char.IsControl);

    private static Value FromValue(Value value, Func<string, Exception> tooDeep, int nestingLimit, int depth)
    {
        if (depth >= nestingLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw tooDeep("This value is nested too deeply to become data.");
        }

        Value Inner(Value part) => FromValue(part, tooDeep, nestingLimit, depth + 1);

        switch (value)
        {
            case ChoiceValue choice when choice.Choice == DataType:
                return choice;
            case IntValue:
                return Carrying(Int, value);
            case FloatValue:
                return Carrying(Float, value);
            case StringValue:
                return Carrying(Text, value);
            case BoolValue:
                return Carrying(Bool, value);
            case ListValue list:
                return OfList([.. list.Items.Select(Inner)]);
            case OptionValue option:
                return option.Content is null ? NullValue : Inner(option.Content);
            case MapValue map:
                var entries = MapValue.Empty(MapType);
                foreach (var (key, stored) in map.Entries)
                {
                    entries = entries.Put(new StringValue(key.ToText()), Inner(stored));
                }

                return OfMap(entries);
            case RecordValue record:
                var fields = MapValue.Empty(MapType);
                foreach (var (field, stored) in record.Record.Fields.Zip(record.Fields))
                {
                    if (stored is not OptionValue { Content: null })
                    {
                        fields = fields.Put(new StringValue(field.Name), Inner(stored));
                    }
                }

                return OfMap(fields);
            case ChoiceValue choice when !choice.HasData:
                return Carrying(Text, new StringValue(choice.Name));
            case ChoiceValue choice:
                var carried = choice.Data.Length == 1
                    ? Inner(choice.Data[0])
                    : OfList([.. choice.Data.Select(Inner)]);
                return OfMap(MapValue.Empty(MapType).Put(new StringValue(choice.Name), carried));
            default:
                throw new InvalidOperationException($"Value of type {value.Type.Name} has no data form.");
        }
    }

    private sealed class Reading(Func<string, Exception> fail, Func<string, Exception> tooDeep, int nestingLimit)
    {
        public Value As(ChoiceValue data, PyropType target, string path, int depth)
        {
            if (depth >= nestingLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw tooDeep("This data is nested too deeply to read.");
            }

            if (target == DataType)
            {
                return data;
            }

            if (target == PyropType.Int || target == PyropType.String || target == PyropType.Bool)
            {
                var wanted = target == PyropType.Int ? Int : target == PyropType.String ? Text : Bool;
                return data.Index == wanted ? data.Data[0] : throw Mismatch(data, target, path);
            }

            if (target == PyropType.Float)
            {
                return data.Index switch
                {
                    Float => data.Data[0],
                    Int => new FloatValue(data.Data[0].AsInt()),
                    _ => throw Mismatch(data, target, path),
                };
            }

            if (PyropTypes.IsOption(target))
            {
                return data.Index == Null
                    ? OptionValue.None(target)
                    : OptionValue.Some(As(data, ((ConstructedType)target).Arguments[0], path, depth + 1));
            }

            if (PyropTypes.IsList(target))
            {
                return data.Index == List ? ReadList(data, target, path, depth) : throw Mismatch(data, target, path);
            }

            if (PyropTypes.IsMap(target))
            {
                return data.Index == Map ? ReadMap(data, target, path, depth) : throw Mismatch(data, target, path);
            }

            return target switch
            {
                RecordType record => data.Index == Map ? ReadRecord(data, record, path, depth) : throw Mismatch(data, target, path),
                ChoiceType choice => ReadChoice(data, choice, path, depth),
                _ => throw new InvalidOperationException($"Data cannot become {target.Name}; the type checker should have rejected this."),
            };
        }

        private Value ReadList(ChoiceValue data, PyropType target, string path, int depth)
        {
            var element = ((ConstructedType)target).Arguments[0];
            var items = data.Data[0].AsList().Items;
            return new ListValue(target, [.. items.Select((item, index) => As((ChoiceValue)item, element, Join(path, index.ToString(CultureInfo.InvariantCulture)), depth + 1))]);
        }

        private Value ReadMap(ChoiceValue data, PyropType target, string path, int depth)
        {
            var arguments = ((ConstructedType)target).Arguments;
            var map = MapValue.Empty(target);
            foreach (var (key, stored) in data.Data[0].AsMap().Entries)
            {
                var name = key.AsString();
                var read = ReadKey(name, arguments[0], path);
                if (map.ContainsKey(read))
                {
                    throw fail($"{Where(path)} has two keys that read as {read.ToText()}.");
                }

                map = map.Put(read, As((ChoiceValue)stored, arguments[1], Join(path, name), depth + 1));
            }

            return map;
        }

        private Value ReadKey(string name, PyropType type, string path)
        {
            if (type == PyropType.String)
            {
                return new StringValue(name);
            }

            if (type == PyropType.Int && long.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                return new IntValue(number);
            }

            if (type == PyropType.Bool && name is "true" or "false")
            {
                return BoolValue.Of(name == "true");
            }

            if (type is ChoiceType choice && choice.FindVariant(name) is { } variant)
            {
                return choice.ValueOf(variant.Index);
            }

            throw fail($"{Where(path)} has the key \"{name}\", which is not {PyropTypes.NamesOf(type)[0]}.");
        }

        private Value ReadRecord(ChoiceValue data, RecordType record, string path, int depth)
        {
            var map = data.Data[0].AsMap();
            var fields = ImmutableArray.CreateBuilder<Value>(record.Fields.Count);
            foreach (var field in record.Fields)
            {
                if (map.Find(new StringValue(field.Name)) is ChoiceValue stored)
                {
                    fields.Add(As(stored, field.Type, Join(path, field.Name), depth + 1));
                }
                else if (PyropTypes.IsOption(field.Type))
                {
                    fields.Add(OptionValue.None(field.Type));
                }
                else
                {
                    throw fail($"{Where(path)} has no field \"{field.Name}\", which {record.Name} needs.");
                }
            }

            return new RecordValue(record, fields.MoveToImmutable());
        }

        private Value ReadChoice(ChoiceValue data, ChoiceType choice, string path, int depth)
        {
            if (data.Index == Text)
            {
                var name = data.Data[0].AsString();
                return choice.FindVariant(name) switch
                {
                    { Arity: 0 } variant => choice.ValueOf(variant.Index),
                    { } carrying => throw fail($"{Where(path)} names the variant {name}, which carries data."),
                    null => throw fail($"{Where(path)} is \"{name}\", which is not a variant of {choice.Name}."),
                };
            }

            if (data.Index != Map || data.Data[0].AsMap().Count != 1)
            {
                throw fail($"{Where(path)} is {KindNames[data.Index]}, not a variant of {choice.Name}.");
            }

            var (key, stored) = data.Data[0].AsMap().Entries.Single();
            var found = choice.FindVariant(key.AsString()) ?? throw fail($"{Where(path)} names \"{key.AsString()}\", which is not a variant of {choice.Name}.");
            if (found.Arity == 0)
            {
                throw fail($"{Where(path)} gives data to the variant {found.Name}, which carries none.");
            }

            var inner = Join(path, found.Name);
            var content = (ChoiceValue)stored;
            if (found.Arity == 1)
            {
                return new ChoiceValue(choice, found.Index, [As(content, found.Data[0], inner, depth + 1)]);
            }

            if (content.Index != List || content.Data[0].AsList().Count != found.Arity)
            {
                throw fail($"{Where(inner)} must be a list of {found.Arity} values for the variant {found.Name}.");
            }

            var parts = content.Data[0].AsList().Items.Select((part, index) => As((ChoiceValue)part, found.Data[index], Join(inner, index.ToString(CultureInfo.InvariantCulture)), depth + 1));
            return new ChoiceValue(choice, found.Index, [.. parts]);
        }

        private Exception Mismatch(ChoiceValue data, PyropType target, string path) =>
            fail($"{Where(path)} is {KindNames[data.Index]}, not {PyropTypes.NamesOf(target)[0]}.");

        private static string Join(string path, string part) => path.Length == 0 ? part : $"{path}.{part}";
    }

    private sealed class YamlReader(IBuiltinInvocation invocation, string text)
    {
        private readonly YamlDotNet.Core.Parser _parser = new(new StringReader(text));
        private readonly Dictionary<string, (Value Node, long Size)> _anchors = [];
        private long _count;

        public Value Read()
        {
            _parser.Consume<StreamStart>();
            if (_parser.Accept<StreamEnd>(out _))
            {
                return NullValue;
            }

            _parser.Consume<DocumentStart>();
            var root = ReadNode(0);
            if (!_parser.Accept<DocumentEnd>(out _))
            {
                throw invocation.Fail($"The text is not valid YAML (line {_parser.Current!.Start.Line}).");
            }

            return root;
        }

        private void Count(long nodes)
        {
            _count += nodes;
            if (_count > invocation.CollectionLimit)
            {
                throw invocation.Fail($"The YAML text expands to more than {invocation.CollectionLimit.ToString("N0", CultureInfo.InvariantCulture)} values.");
            }
        }

        private Value ReadNode(int depth)
        {
            GuardYamlDepth(invocation, depth);
            var before = _count;
            var current = _parser.Current!;
            Value node;
            AnchorName anchor;
            switch (current)
            {
                case AnchorAlias alias:
                    if (!_anchors.TryGetValue(alias.Value.Value, out var known))
                    {
                        throw invocation.Fail($"The YAML text uses the alias *{alias.Value.Value} before any anchor of that name is finished.");
                    }

                    _parser.MoveNext();
                    Count(known.Size);
                    return known.Node;
                case Scalar scalar:
                    Count(1);
                    RequireKnownTag(scalar.Tag, scalar.Start);
                    _parser.MoveNext();
                    node = ValueOf(scalar);
                    anchor = scalar.Anchor;
                    break;
                case SequenceStart sequence:
                    Count(1);
                    RequireKnownTag(sequence.Tag, sequence.Start);
                    _parser.MoveNext();
                    node = ReadSequence(depth);
                    anchor = sequence.Anchor;
                    break;
                case MappingStart mapping:
                    Count(1);
                    RequireKnownTag(mapping.Tag, mapping.Start);
                    _parser.MoveNext();
                    node = ReadMapping(depth);
                    anchor = mapping.Anchor;
                    break;
                default:
                    throw invocation.Fail($"The text is not valid YAML (line {current.Start.Line}).");
            }

            if (!anchor.IsEmpty)
            {
                _anchors[anchor.Value] = (node, _count - before);
            }

            return node;
        }

        private ChoiceValue ReadSequence(int depth)
        {
            var items = ImmutableList.CreateBuilder<Value>();
            while (!_parser.Accept<SequenceEnd>(out _))
            {
                items.Add(ReadNode(depth + 1));
            }

            _parser.MoveNext();
            return OfList(items.ToImmutable());
        }

        private ChoiceValue ReadMapping(int depth)
        {
            var map = MapValue.Empty(MapType);
            while (!_parser.Accept<MappingEnd>(out _))
            {
                if (_parser.Current is not Scalar keyEvent)
                {
                    throw invocation.Fail($"A YAML key must be plain text (line {_parser.Current!.Start.Line}).");
                }

                Count(1);
                RequireKnownTag(keyEvent.Tag, keyEvent.Start);
                _parser.MoveNext();
                if (!keyEvent.Anchor.IsEmpty)
                {
                    _anchors[keyEvent.Anchor.Value] = (ValueOf(keyEvent), 1);
                }

                var key = new StringValue(keyEvent.Value);
                if (map.ContainsKey(key))
                {
                    throw invocation.Fail($"The YAML text repeats the key \"{keyEvent.Value}\" (line {keyEvent.Start.Line}).");
                }

                map = map.Put(key, ReadNode(depth + 1));
            }

            _parser.MoveNext();
            return OfMap(map);
        }

        private void RequireKnownTag(TagName tag, Mark start)
        {
            if (tag.IsEmpty || tag.IsNonSpecific)
            {
                return;
            }

            if (!tag.Value.StartsWith(CoreTagPrefix, StringComparison.Ordinal) || !CoreTags.Contains(tag.Value[CoreTagPrefix.Length..]))
            {
                throw invocation.Fail($"The YAML text uses the tag {tag.Value}, which Pyrop does not read (line {start.Line}).");
            }
        }

        private Value ValueOf(Scalar scalar)
        {
            if (scalar.Style != ScalarStyle.Plain || (!scalar.Tag.IsEmpty && (scalar.Tag.IsNonSpecific || scalar.Tag.Value == CoreTagPrefix + "str")))
            {
                return Carrying(Text, new StringValue(scalar.Value));
            }

            var plain = scalar.Value;
            if (plain is "" or "~" or "null" or "Null" or "NULL")
            {
                return NullValue;
            }

            if (plain is "true" or "True" or "TRUE" or "false" or "False" or "FALSE")
            {
                return Carrying(Bool, BoolValue.Of(char.ToLowerInvariant(plain[0]) == 't'));
            }

            if (plain.TrimStart('+', '-') is ".inf" or ".Inf" or ".INF" || plain is ".nan" or ".NaN" or ".NAN")
            {
                throw invocation.Fail($"Pyrop floats are always finite, so the YAML value {plain} cannot be read (line {scalar.Start.Line}).");
            }

            if (long.TryParse(plain, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
            {
                return Carrying(Int, new IntValue(whole));
            }

            if (ReadPrefixed(plain) is { } prefixed)
            {
                return Carrying(Int, new IntValue(prefixed));
            }

            if (LooksLikeFloat(plain))
            {
                return double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                    ? Carrying(Float, new FloatValue(number))
                    : throw invocation.Fail($"The number {plain} is too large to read (line {scalar.Start.Line}).");
            }

            return Carrying(Text, new StringValue(plain));
        }

        private static long? ReadPrefixed(string plain)
        {
            if (plain.Length <= 2 || plain[0] != '0')
            {
                return null;
            }

            var digits = plain[2..];
            try
            {
                return plain[1] switch
                {
                    'o' when digits.All(digit => digit is >= '0' and <= '7') => Convert.ToInt64(digits, 8),
                    'x' when digits.All(char.IsAsciiHexDigit) && digits.Length <= 16 && digits.TrimStart('0').Length <= 16
                        && (digits.TrimStart('0').Length < 16 || digits.TrimStart('0')[0] < '8') => Convert.ToInt64(digits, 16),
                    _ => null,
                };
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        private static bool LooksLikeFloat(string plain)
        {
            var index = plain[0] is '+' or '-' ? 1 : 0;
            var start = index;
            while (index < plain.Length && char.IsAsciiDigit(plain[index]))
            {
                index++;
            }

            var digits = index - start;
            if (index < plain.Length && plain[index] == '.')
            {
                index++;
                var fraction = index;
                while (index < plain.Length && char.IsAsciiDigit(plain[index]))
                {
                    index++;
                }

                digits += index - fraction;
            }

            if (digits == 0)
            {
                return false;
            }

            if (index < plain.Length && plain[index] is 'e' or 'E')
            {
                index++;
                if (index < plain.Length && plain[index] is '+' or '-')
                {
                    index++;
                }

                var exponent = index;
                while (index < plain.Length && char.IsAsciiDigit(plain[index]))
                {
                    index++;
                }

                if (index == exponent)
                {
                    return false;
                }
            }

            return index == plain.Length;
        }
    }
}
