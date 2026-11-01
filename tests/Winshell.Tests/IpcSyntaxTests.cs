using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>A wrong <c>ipc</c> command line is a usage error, whether or not anything is running.</summary>
public sealed class IpcSyntaxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public IpcSyntaxTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("call", "foo")]
    [InlineData("prop", "get", "foo")]
    [InlineData("wait", "foo")]
    [InlineData("nonsense")]
    public void A_wrong_ipc_command_exits_2_with_nothing_running(params string[] words)
    {
        var (code, output, error) = WinshellCli.Run(["ipc", .. words], ("WINSHELL_LOCAL_DIR", _root));
        var text = output + error;

        Assert.Equal(2, code);
        Assert.Contains("Usage: winshell ipc", text);
        Assert.DoesNotContain("No running instances", text);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
