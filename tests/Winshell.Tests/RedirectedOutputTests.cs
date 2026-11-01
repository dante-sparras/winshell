using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>Redirected output is UTF-8, whatever the console's code page is, so a pipe or file gets the text intact.</summary>
public sealed class RedirectedOutputTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private Process? _shell;

    [Fact]
    public void Text_outside_the_console_code_page_survives_stdout_and_stderr()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "shell.qml"), """
            import Winshell
            import Winshell.Io

            ShellRoot {
              IpcHandler {
                target: "probe"
                function song(): string { return "\u{1F3B5} Björk 東京" }
                function boom(): string { throw new Error("\u{1F3B5} failed") }
              }
            }
            """, new UTF8Encoding(false));
        Start();
        var pid = _shell!.Id.ToString(CultureInfo.InvariantCulture);

        var song = Run("ipc", "--pid", pid, "call", "probe", "song");
        var boom = Run("ipc", "--pid", pid, "call", "probe", "boom");

        Assert.Equal(0, song.Code);
        Assert.Equal("\U0001F3B5 Björk 東京", song.Output.TrimEnd());
        Assert.Equal(1, boom.Code);
        Assert.Contains("\U0001F3B5 failed", boom.Error, StringComparison.Ordinal);
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

    private void Start()
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(_dir);
        _shell = Process.Start(info)!;
        _shell.ErrorDataReceived += (_, _) => { };
        _shell.BeginErrorReadLine();
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == _shell.Id), $"instance {_shell.Id} in winshell list");
    }

    private static List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static (int Code, string Output, string Error) Run(params string[] args) => WinshellCli.Run(args);

    public void Dispose()
    {
        if (_shell is not null)
        {
            if (!_shell.HasExited)
            {
                Run("kill", "--pid", _shell.Id.ToString(CultureInfo.InvariantCulture));
                if (!_shell.WaitForExit(5000))
                    _shell.Kill();
                _shell.WaitForExit();
            }
            _shell.Dispose();
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
