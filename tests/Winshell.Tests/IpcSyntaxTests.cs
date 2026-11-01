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
    public async Task A_wrong_ipc_command_exits_2_with_nothing_running(params string[] words)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winshell.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("ipc");
        foreach (var word in words)
            info.ArgumentList.Add(word);
        info.Environment["WINSHELL_LOCAL_DIR"] = _root;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.True(process.WaitForExit(30000), "winshell did not exit");
        var text = await output + await error;

        Assert.Equal(2, process.ExitCode);
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
