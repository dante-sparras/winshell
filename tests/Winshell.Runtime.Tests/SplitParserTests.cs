using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Line splitting keeps a CRLF that arrives in two chunks, and scans each chunk once.</summary>
public class SplitParserTests
{
    [Fact]
    public void A_crlf_split_across_chunks_is_one_line_break()
    {
        using var shell = new TestShell();
        var parser = new SplitParser { Engine = shell.Engine };
        var lines = new List<string>();
        parser.Connect("read", args => lines.Add((string)args[0]!));

        parser.Feed("hello\r");
        Assert.Empty(lines);

        parser.Feed("\nworld\r\n");
        parser.Feed("tail");
        parser.End();

        Assert.Equal(["hello", "world", "tail"], lines);
    }

    [Fact]
    public void A_trailing_cr_without_a_following_lf_stays_in_the_line()
    {
        using var shell = new TestShell();
        var parser = new SplitParser { Engine = shell.Engine };
        var lines = new List<string>();
        parser.Connect("read", args => lines.Add((string)args[0]!));

        parser.Feed("hello\r");
        parser.End();

        Assert.Equal(["hello\r"], lines);
    }

    [Fact]
    public void Many_lines_in_one_chunk_are_emitted_in_order()
    {
        using var shell = new TestShell();
        var parser = new SplitParser { Engine = shell.Engine };
        var lines = new List<string>();
        parser.Connect("read", args => lines.Add((string)args[0]!));

        parser.Feed("a\r\nb\nc\r\n");
        parser.End();

        Assert.Equal(["a", "b", "c"], lines);
    }
}
