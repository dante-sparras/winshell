using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>Redirected output is UTF-8, whatever the console's code page is, so a pipe or file gets the text intact.</summary>
[Trait("Category", "Slow")]
public sealed class RedirectedOutputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Text_outside_the_console_code_page_survives_stdout_and_stderr()
    {
        Directory.CreateDirectory(_dir);
        var missing = Path.Combine(_dir, "Björk 東京");

        var run = WinshellCli.Run(["run", "-p", missing], ("WINSHELL_CONFIG_DIR", _dir));
        var doctor = WinshellCli.Run(["doctor", "-p", missing], ("WINSHELL_CONFIG_DIR", _dir));

        Assert.Equal(1, run.Code);
        Assert.Contains("Björk 東京", run.Error, StringComparison.Ordinal);
        Assert.Contains("Björk 東京", doctor.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Redirected_output_has_no_byte_order_mark()
    {
        var info = WinshellCli.StartInfo(["--version"]);
        using var process = Process.Start(info)!;
        using var bytes = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(bytes);
        process.WaitForExit();

        Assert.Equal((byte)'w', bytes.ToArray()[0]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
