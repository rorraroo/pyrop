using System.Diagnostics;
using Pyrop.Cli;
using Pyrop.Runtime;

namespace Pyrop.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceCollection
{
    public const string Name = "Performance";
}

[Collection(PerformanceCollection.Name)]
public sealed class PerformanceTests
{
    private static TimeSpan Timed(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed;
    }

    [Fact]
    public void ReplacingANestedValueInAMapIsFast()
    {
        const string source =
            "define list int big list_of int end\ndefine int i 0\n" +
            "while less i 200000 block\n    set big push big i\n    set i add i 1\nend\n" +
            "define map int list int m put map_of int list int end 1 big\n" +
            "set i 0\nwhile less i 2000 block\n    set m put m 1 put get m 1 199999 i\n    set i add i 1\nend\n" +
            "write_line get get m 1 199999";
        var output = "";

        var elapsed = Timed(() => output = Run.Program(source));

        Assert.Equal("1999\n", output);
        Assert.True(elapsed < TimeSpan.FromSeconds(15), $"Took {elapsed}.");
    }

    [Fact]
    public void PushingAHundredThousandItemsIsFast()
    {
        const string source =
            "define list int a list_of int end\ndefine int i 0\n" +
            "while less i 100000 block\n    set a push a i\n    set i add i 1\nend\n" +
            "write_line length_of a\nwrite_line get a 99999";
        Run.Program(source);
        var output = "";

        var elapsed = Timed(() => output = Run.Program(source));

        Assert.Equal("100000\n99999\n", output);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Took {elapsed}.");
    }

    [Fact]
    public void WalksALargeListQuickly()
    {
        const string source = """
            define list int numbers list_of int end
            define int i 0
            while less i 100000 block
                set numbers push numbers i
                set i add i 1
            end
            define int total 0
            for int n in numbers set total add total n
            write_line total
            """;
        var output = "";

        var elapsed = Timed(() => output = Run.Program(source));

        Assert.Equal("4999950000\n", output);
        Assert.True(elapsed < TimeSpan.FromSeconds(20), $"Took {elapsed}.");
    }

    [Fact]
    public void YamlNestedPastAThousandLevelsIsRefusedQuickly()
    {
        PyropError? error = null;

        var elapsed = Timed(() => error = Run.Failure("use data library\nwrite_line from data json_text from data parse_yaml repeat \"[\" 2000000"));

        Assert.Equal("YAML can nest at most 1,000 levels deep.", error!.Message);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Took {elapsed}.");
    }

    [Fact]
    public void ALongWordAndALargeTableLayOutQuickly()
    {
        var rows = Enumerable.Range(0, 20000).Select(index => (IReadOnlyList<string>)[$"row {index}", "x"]).ToList();

        var elapsed = Timed(() =>
        {
            ViewLayout.Screen(new StackView(false, [new TextView(new string('a', 100000)), new TableView(["a", "b"], rows, [])]), OutputStyle.None, 80, 30);
            ViewLayout.Page(new TableView(["a", "b"], rows, []), OutputStyle.None, 80);
        });

        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Took {elapsed}.");
    }

    [Fact]
    public void SplittingALongTextStaysFast()
    {
        var output = "";

        var elapsed = Timed(() => output = Run.Program(
            "define string text repeat \"word \" 200000\nwrite_line length_of split text \" \"\nwrite_line length_of join split text \" \" \" \""));

        Assert.Equal("200001\n1000000\n", output);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Took {elapsed}.");
    }
}
