using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>list --json</c> is a bare array of the command's own fields, and <c>ipc show -j</c> is JSON for the
/// targets. Neither is the on-disk instance file.
/// </summary>
[Collection(DefaultShellCollection.Name)]
[Trait("Category", "Slow")]
public sealed class CliJsonOutputTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];
    private readonly List<Process> _runs = [];
    private string? _local;

    [Fact]
    public void List_json_drops_file_fields_and_ipc_show_json_lists_the_target()
    {
        var local = Path.Combine(_dir, "café");
        Directory.CreateDirectory(local);
        var process = Start(local);

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
        Assert.Contains("café", row.GetProperty("log").GetString(), StringComparison.Ordinal);
        _ = row.GetProperty("launchTime").GetDateTime();
        Assert.False(string.IsNullOrEmpty(row.GetProperty("pipe").GetString()));
        Assert.False(string.IsNullOrEmpty(row.GetProperty("log").GetString()));

        Wait.Until(() => Run("ipc", "--pid", process.Id.ToString(CultureInfo.InvariantCulture), "show", "-j").Output.Contains("settings", StringComparison.Ordinal), "the settings target");
        var show = Run("ipc", "--pid", process.Id.ToString(CultureInfo.InvariantCulture), "show", "-j");
        Assert.Equal(0, show.Code);
        Assert.DoesNotContain("\\u00E9", show.Output, StringComparison.OrdinalIgnoreCase);
        using var shown = JsonDocument.Parse(show.Output);
        Assert.Equal(JsonValueKind.Array, shown.RootElement.ValueKind);
        var target = shown.RootElement.EnumerateArray().Single(t => t.GetProperty("target").GetString() == "settings");
        var open = Assert.Single(target.GetProperty("functions").EnumerateArray());
        Assert.Equal("open", open.GetProperty("name").GetString());
        Assert.Equal("void", open.GetProperty("returns").GetString());
        var parameter = Assert.Single(open.GetProperty("parameters").EnumerateArray());
        Assert.Equal("page", parameter.GetProperty("name").GetString());
        Assert.Equal("string", parameter.GetProperty("type").GetString());
    }

    /// <summary>The file a running instance writes still carries the fields the command leaves out.</summary>
    [Fact]
    public void The_instance_file_keeps_process_start_and_exited_at()
    {
        var info = new InstanceInfo("id", 1, "shell", "C:\\cfg\\MyShell.exe", DateTime.Now, "pipe", "C:\\log.txt", 42, DateTime.Now);
        var file = JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo);
        Assert.Contains("\"processStart\": 42", file, StringComparison.Ordinal);
        Assert.Contains("\"exitedAt\":", file, StringComparison.Ordinal);
    }

    private Process Start(string configDir)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("run");
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        _local = configDir;
        info.Environment["WINSHELL_CONFIG_DIR"] = Path.Combine(_dir, "config");
        info.Environment["WINSHELL_LOCAL_DIR"] = configDir;
        var run = Process.Start(info)!;
        _runs.Add(run);
        run.ErrorDataReceived += (_, _) => { };
        run.OutputDataReceived += (_, _) => { };
        run.BeginErrorReadLine();
        run.BeginOutputReadLine();
        // winshell run supervises the shell, which runs in a child process.
        Wait.Until(() => ListedPids().Count > 0, "the shell in winshell list");
        var process = Process.GetProcessById(ListedPids().Single());
        _started.Add(process);
        return process;
    }

    private HashSet<int> ListedPids()
    {
        var list = Run("list", "--json");
        if (list.Code != 0)
            return [];
        using var json = JsonDocument.Parse(list.Output);
        return json.RootElement.EnumerateArray().Select(i => i.GetProperty("pid").GetInt32()).ToHashSet();
    }

    private (int Code, string Output, string Error) Run(params string[] args) =>
        _local is null
            ? WinshellCli.Run(args)
            : WinshellCli.Run(args, ("WINSHELL_LOCAL_DIR", _local), ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")));

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
        // Each ends once its shell has. One that has not would start the shell again.
        foreach (var run in _runs)
        {
            if (!run.WaitForExit(5000))
                run.Kill(entireProcessTree: true);
            run.WaitForExit();
            run.Dispose();
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
