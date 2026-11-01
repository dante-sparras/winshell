using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>list --json</c> is a bare array of the command's own fields, and <c>ipc show -j</c> is JSON for the
/// targets. Neither is the on-disk instance file.
/// </summary>
public sealed class CliJsonOutputTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void List_json_drops_file_fields_and_ipc_show_json_lists_the_target()
    {
        var config = Path.Combine(_dir, "café");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "shell.qml"), """
            import Winshell
            import Winshell.Io

            ShellRoot {
              IpcHandler {
                target: "café"
                property string title: "notes"
                property var extra: 1
                signal opened(query: string)
                signal saved(name, size)
                function hello(name: string): string { return name }
                function take(items: var): void {}
              }
            }
            """);
        var process = Start(config);

        var list = Run("list", "--json");
        Assert.Equal(0, list.Code);
        Assert.Contains("café", list.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00E9", list.Output, StringComparison.OrdinalIgnoreCase);
        using var listed = JsonDocument.Parse(list.Output);
        Assert.Equal(JsonValueKind.Array, listed.RootElement.ValueKind);
        var row = listed.RootElement.EnumerateArray().Single(i => i.GetProperty("pid").GetInt32() == process.Id);
        Assert.False(row.TryGetProperty("processStart", out _));
        Assert.False(row.TryGetProperty("exitedAt", out _));
        Assert.False(string.IsNullOrEmpty(row.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrEmpty(row.GetProperty("shellId").GetString()));
        Assert.Contains("café", row.GetProperty("configPath").GetString(), StringComparison.Ordinal);
        _ = row.GetProperty("launchTime").GetDateTime();
        Assert.False(string.IsNullOrEmpty(row.GetProperty("pipe").GetString()));
        Assert.False(string.IsNullOrEmpty(row.GetProperty("log").GetString()));

        var show = Run("ipc", "--pid", process.Id.ToString(CultureInfo.InvariantCulture), "show", "-j");
        Assert.Equal(0, show.Code);
        Assert.Contains("café", show.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00E9", show.Output, StringComparison.OrdinalIgnoreCase);
        using var shown = JsonDocument.Parse(show.Output);
        Assert.Equal(JsonValueKind.Array, shown.RootElement.ValueKind);
        var target = Assert.Single(shown.RootElement.EnumerateArray());
        Assert.Equal("café", target.GetProperty("target").GetString());
        var functions = target.GetProperty("functions").EnumerateArray().ToList();
        var hello = Assert.Single(functions);
        Assert.Equal("hello", hello.GetProperty("name").GetString());
        Assert.Equal("string", hello.GetProperty("returns").GetString());
        var parameter = Assert.Single(hello.GetProperty("parameters").EnumerateArray());
        Assert.Equal("name", parameter.GetProperty("name").GetString());
        Assert.Equal("string", parameter.GetProperty("type").GetString());
        var properties = target.GetProperty("properties").EnumerateArray().ToList();
        var title = Assert.Single(properties);
        Assert.Equal("title", title.GetProperty("name").GetString());
        Assert.Equal("string", title.GetProperty("type").GetString());
        var signals = target.GetProperty("signals").EnumerateArray().ToList();
        Assert.Equal(2, signals.Count);
        var opened = signals.Single(s => s.GetProperty("name").GetString() == "opened");
        var query = Assert.Single(opened.GetProperty("parameters").EnumerateArray());
        Assert.Equal("query", query.GetProperty("name").GetString());
        Assert.Equal("string", query.GetProperty("type").GetString());
        var saved = signals.Single(s => s.GetProperty("name").GetString() == "saved");
        var savedParameters = saved.GetProperty("parameters").EnumerateArray().ToList();
        Assert.Equal(["name", "size"], savedParameters.Select(p => p.GetProperty("name").GetString()));
        Assert.All(savedParameters, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("type").ValueKind));
    }

    /// <summary>The file a running instance writes still carries the fields the command leaves out.</summary>
    [Fact]
    public void The_instance_file_keeps_process_start_and_exited_at()
    {
        var info = new InstanceInfo("id", 1, "shell", "C:\\cfg\\shell.qml", DateTime.Now, "pipe", "C:\\log.txt", 42, DateTime.Now);
        var file = JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo);
        Assert.Contains("\"processStart\": 42", file, StringComparison.Ordinal);
        Assert.Contains("\"exitedAt\":", file, StringComparison.Ordinal);
    }

    private Process Start(string configDir)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(configDir);
        info.Environment.Remove("WINSHELL_DISABLE_FILE_WATCHER");
        var process = Process.Start(info)!;
        _started.Add(process);
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        Wait.Until(() => ListedPids().Contains(process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private static HashSet<int> ListedPids()
    {
        var list = Run("list", "--json");
        if (list.Code != 0)
            return [];
        using var json = JsonDocument.Parse(list.Output);
        return json.RootElement.EnumerateArray().Select(i => i.GetProperty("pid").GetInt32()).ToHashSet();
    }

    private static (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args);

    public void Dispose()
    {
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                try
                {
                    Run("kill", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                }
                if (!process.WaitForExit(5000))
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
