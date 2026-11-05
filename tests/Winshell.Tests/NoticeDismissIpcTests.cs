using System.Diagnostics;
using System.Globalization;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell ipc call notice dismiss</c> closes whichever notice is showing, and it takes no arguments.
/// </summary>
[Trait("Category", "Slow")]
public sealed class NoticeDismissIpcTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private Process? _started;

    [Fact]
    public void Notice_dismiss_reaches_a_running_shell_and_takes_no_arguments()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var process = _started = ProbeShell.Start(config, Path.Combine(_dir, "local"));

        var pid = process.Id.ToString(CultureInfo.InvariantCulture);
        var dismissed = Run("ipc", "--pid", pid, "call", "notice", "dismiss");
        Assert.Equal("", dismissed.Error);
        Assert.Equal("", dismissed.Output.Trim());
        Assert.Equal(0, dismissed.Code);
        Assert.False(process.HasExited);

        var extra = Run("ipc", "--pid", pid, "call", "notice", "dismiss", "now");
        Assert.Equal(1, extra.Code);
        Assert.Contains("Function notice.dismiss takes 0 arguments, 1 given.", extra.Error, StringComparison.Ordinal);
        Assert.False(process.HasExited);

        var other = Run("ipc", "--pid", pid, "call", "notice", "open");
        Assert.Equal(1, other.Code);
        Assert.Contains("Target not found: notice.", other.Error, StringComparison.Ordinal);
        Assert.False(process.HasExited);
    }

    private (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args, ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")), ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")));

    public void Dispose()
    {
        if (_started is { } process)
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
            }
            process.Dispose();
        }
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
