namespace Winshell.Runtime.Tests;

public class PreprocessorLocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_and_inline_documents_name_their_source_in_warnings(bool inline)
    {
        using var shell = new TestShell();
        const string text = "//@ iff true\nimport QtQuick\nItem {}";
        var document = inline
            ? shell.Engine.LoadInline(text, shell.Directory)
            : shell.Engine.LoadDocumentFromText(text, Path.Combine(shell.Directory, "virtual.qml"));

        var message = Assert.Single(shell.Messages);
        Assert.Contains($"{document.File}[1:1]: unknown preprocessor directive 'iff true'", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ShellId demo")]
    [InlineData("AppId demo")]
    [InlineData("DataDir data")]
    [InlineData("StateDir state")]
    [InlineData("CacheDir cache")]
    [InlineData("Env NAME=value")]
    public void Documented_pragmas_pass_through_without_warnings(string pragma)
    {
        var warnings = new List<string>();
        var text = $"//@ pragma {pragma}\nItem {{}}";

        Assert.Equal(text, Preprocessor.Run(text, "shell.qml", warnings));
        Assert.Empty(warnings);
    }
}
