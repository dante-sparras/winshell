namespace Winshell.Tests;

/// <summary>A wrong <c>ipc</c> command line is a usage error, whether or not anything is running.</summary>
[Trait("Category", "Slow")]
public sealed class IpcSyntaxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public IpcSyntaxTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData("winshell ipc call needs a target and a function.", "call", "foo")]
    [InlineData("winshell ipc prop needs \"get\", a target and a property.", "prop", "get", "foo")]
    [InlineData("winshell ipc wait needs a target and a signal.", "wait", "foo")]
    public void A_wrong_ipc_command_exits_2_with_nothing_running(string message, params string[] words)
    {
        var result = WinshellCli.Run(["ipc", .. words], ("WINSHELL_LOCAL_DIR", _root));

        result.AssertExitCode(2);
        Assert.Equal("", result.Output);
        Assert.Equal(message + Environment.NewLine, result.Error);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
