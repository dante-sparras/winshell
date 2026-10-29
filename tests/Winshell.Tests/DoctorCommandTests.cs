using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary><c>winshell doctor</c> as a process. Every config is under a temp folder this class creates.</summary>
public sealed class DoctorCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-doctor-tests", Guid.NewGuid().ToString("N"));

    public DoctorCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Json_lists_every_check_and_a_broken_config_fails()
    {
        var broken = Path.Combine(_root, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "shell.qml"), "import QtQuick\nItem {\n");

        var (code, output, _) = Run("doctor", "--json", "-p", broken);

        Assert.Equal(1, code);
        using var json = JsonDocument.Parse(output);
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(
            ["windows", "dotnet", "config", "folders", "appBars", "dpi", "signIn", "notifications", "instances", "crashes"],
            checks.Select(c => c.GetProperty("id").GetString()));
        var config = checks.Single(c => c.GetProperty("id").GetString() == "config");
        Assert.Equal("fail", config.GetProperty("status").GetString());
        Assert.Contains("shell.qml", config.GetProperty("message").GetString());
        Assert.NotNull(config.GetProperty("fix").GetString());
        Assert.True(json.RootElement.GetProperty("summary").GetProperty("fail").GetInt32() >= 1);
    }

    [Fact]
    public void Text_has_a_line_a_check_and_no_color_when_redirected()
    {
        var good = Path.Combine(_root, "good");
        Directory.CreateDirectory(good);
        File.WriteAllText(Path.Combine(good, "shell.qml"), "import QtQuick\nItem {}\n");

        var (_, output, _) = Run("doctor", "-p", good);

        Assert.DoesNotContain("\e[", output);
        Assert.Contains("  PASS  Config", output);
        Assert.Matches(@"\d+ passed, \d+ warnings?, \d+ failed\.", output);
    }

    [Fact]
    public void Without_a_config_it_checks_the_default_shell_and_writes_nothing()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var (_, output, _) = RunIn(empty, "doctor", "--json");

        using var json = JsonDocument.Parse(output);
        var config = json.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "config");
        Assert.Equal("pass", config.GetProperty("status").GetString());
        Assert.Contains("default shell", config.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(empty));
    }

    [Fact]
    public void A_config_folder_it_cannot_list_fails_the_config_check_and_the_others_still_run()
    {
        var unreadable = Path.Combine(_root, "unreadable");
        Directory.CreateDirectory(unreadable);
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var folder = new DirectoryInfo(unreadable);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        (int Code, string Output, string Error) result;
        try
        {
            result = RunIn(unreadable, "doctor", "--json");
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }

        Assert.Equal(1, result.Code);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(10, checks.Count);
        var config = checks.Single(c => c.GetProperty("id").GetString() == "config");
        Assert.Equal("fail", config.GetProperty("status").GetString());
        Assert.StartsWith("Winshell cannot read the config folder", config.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private (int Code, string Output, string Error) Run(params string[] args) => RunIn(_root, args);

    /// <summary>Runs winshell.exe with the config folder overridden to a folder of this class's.</summary>
    private static (int Code, string Output, string Error) RunIn(string configRoot, params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winshell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment[ConfigPaths.OverrideVariable] = configRoot;
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result, error.Result);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
