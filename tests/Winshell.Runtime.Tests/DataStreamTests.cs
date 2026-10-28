using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class DataStreamTests
{
    private sealed class Probe : DataStream
    {
        public void Push(string text) => Deliver(text);

        public void Finish() => EndStream();
    }

    [Fact]
    public void Text_waits_for_a_parser_and_then_arrives_as_lines()
    {
        using var shell = new TestShell();
        var stream = new Probe { Engine = shell.Engine };
        var parser = new SplitParser { Engine = shell.Engine };
        var lines = new List<string>();
        parser.Connect("read", args => lines.Add((string)args[0]!));

        stream.Push("hello\nwor");
        Assert.Empty(lines);

        stream.Parser = parser;

        Assert.Equal("hello", Assert.Single(lines));
        Assert.Same(stream, parser.Owner);

        stream.Push("ld\n");
        stream.Push("tail");
        stream.Finish();

        Assert.Equal(["hello", "world", "tail"], lines);
    }
}
