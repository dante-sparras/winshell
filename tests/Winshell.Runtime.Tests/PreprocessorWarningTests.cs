namespace Winshell.Runtime.Tests;

public class PreprocessorWarningTests
{
    [Fact]
    public void A_script_expression_does_not_run_and_is_reported()
    {
        var warnings = new List<string>();
        var text = Preprocessor.Run("//@ if (function f(){ while(1); })()\nkept\n//@ endif", "shell.qml", warnings);
        Assert.Equal(["//@ if (function f(){ while(1); })()", "", "//@ endif"], text.Split('\n'));
        Assert.Contains(warnings, warning => warning.StartsWith("shell.qml[1:1]: ", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_directive_and_an_unclosed_if_are_reported()
    {
        var warnings = new List<string>();
        var text = Preprocessor.Run("//@ iff true\nkept\n//@ if true\nstill", "shell.qml", warnings);
        Assert.Contains("kept", text, StringComparison.Ordinal);
        Assert.Contains("still", text, StringComparison.Ordinal);
        Assert.Contains(warnings, warning => warning.Contains("unknown preprocessor directive", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("was not closed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pragma_stays_in_the_file_and_is_not_a_warning()
    {
        var warnings = new List<string>();
        var text = Preprocessor.Run("//@ pragma ShellId demo\nItem {}", "shell.qml", warnings);
        Assert.Contains("//@ pragma ShellId demo", text, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    [Fact]
    public void An_else_without_an_if_is_reported()
    {
        var warnings = new List<string>();
        Preprocessor.Run("//@ else\nkept", "shell.qml", warnings);
        Assert.Contains(warnings, warning => warning.Contains("else without a matching if", StringComparison.Ordinal));
    }

    [Fact]
    public void A_loaded_file_names_itself_in_its_warnings_and_a_pragma_is_quiet()
    {
        using var shell = new TestShell(("shell.qml", "//@ pragma ShellId demo\n//@ iff true\nimport QtQuick\nItem {}\n"));
        shell.Load();
        var path = Path.Combine(shell.Directory, "shell.qml");
        var warning = Assert.Single(shell.Messages, message => message.Contains("preprocessor", StringComparison.Ordinal));
        Assert.Contains($"{path}[2:1]: unknown preprocessor directive 'iff true'", warning, StringComparison.Ordinal);
    }
}
