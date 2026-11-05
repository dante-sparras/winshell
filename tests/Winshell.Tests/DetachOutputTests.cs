namespace Winshell.Tests;

/// <summary>
/// A <c>-d</c> start returns at once, and the shell it leaves behind holds nothing of its caller: output read to the end
/// ends when the start does. The probe has no windows.
/// </summary>
[Trait("Category", "Slow")]
public sealed class DetachOutputTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    private (string, string)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")),
    ];

    public DetachOutputTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        Directory.CreateDirectory(Path.Combine(_dir, "local"));
    }

    [Fact]
    public void The_output_of_run_d_ends_when_it_returns()
    {
        // The probe's program is <project>\bin\shell\<name>.exe.
        var project = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ProbeShell.Program)))!;

        // WinshellCli reads both streams to their end, and fails if they stay open after the process exits.
        var (code, _, error) = WinshellCli.Run(["run", "-d", "--no-build", "-p", project], Folders);

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Wait.Until(() => Running().Count > 0, "the shell -d left behind");
    }

    private List<int> Running()
    {
        using var listed = System.Text.Json.JsonDocument.Parse(WinshellCli.Run(["list", "-j"], Folders).Output);
        return listed.RootElement.EnumerateArray().Select(row => row.GetProperty("pid").GetInt32()).ToList();
    }

    public void Dispose()
    {
        // Asked, not ended: the winshell run that supervises the shell would start an ended one again.
        foreach (var pid in Running())
            WinshellCli.Run(["kill", "--force", "--pid", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)], Folders);
        Wait.Until(Removed, "the test folder to be free");
    }

    private bool Removed()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
