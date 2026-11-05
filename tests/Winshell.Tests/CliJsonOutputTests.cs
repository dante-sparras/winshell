using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>list --json</c> is a bare array of the command's own fields, and <c>ipc show -j</c> is JSON for the
/// targets. Neither is the on-disk instance file.
/// </summary>
[Trait("Category", "Slow")]
public sealed class CliJsonOutputTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private Process? _started;

    private string Local => Path.Combine(_dir, "café");

    [Fact]
    public void List_json_drops_file_fields_and_ipc_show_json_lists_the_target()
    {
        Directory.CreateDirectory(Local);
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        var process = _started = ProbeShell.Start(Path.Combine(_dir, "config"), Local);

        var list = Run("list", "--json");
        list.AssertExitCode(0);
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

        var show = Run("ipc", "--pid", process.Id.ToString(CultureInfo.InvariantCulture), "show", "-j");
        show.AssertExitCode(0);
        Assert.DoesNotContain("\\u00E9", show.Output, StringComparison.OrdinalIgnoreCase);
        using var shown = JsonDocument.Parse(show.Output);
        Assert.Equal(JsonValueKind.Array, shown.RootElement.ValueKind);
        var target = shown.RootElement.EnumerateArray().Single(t => t.GetProperty("target").GetString() == "probe");
        var note = Assert.Single(target.GetProperty("functions").EnumerateArray());
        Assert.Equal("note", note.GetProperty("name").GetString());
        Assert.Equal("void", note.GetProperty("returns").GetString());
        var parameter = Assert.Single(note.GetProperty("parameters").EnumerateArray());
        Assert.Equal("text", parameter.GetProperty("name").GetString());
        Assert.Equal("string", parameter.GetProperty("type").GetString());
    }

    /// <summary>The file a running instance writes still carries the fields the command leaves out.</summary>
    [Fact]
    public void The_instance_file_keeps_process_start_and_exited_at()
    {
        var info = new InstanceInfo("id", 1, "shell", "C:\\cfg\\MyShell.exe", DateTime.Now, "pipe", "C:\\log.txt", 42,
            DateTime.Now);
        var file = JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo);
        Assert.Contains("\"processStart\": 42", file, StringComparison.Ordinal);
        Assert.Contains("\"exitedAt\":", file, StringComparison.Ordinal);
    }

    private ChildProcess Run(params string[] args)
    {
        return WinshellCli.Run(args, ("WINSHELL_LOCAL_DIR", Local),
            ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")));
    }

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
