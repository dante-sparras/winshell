namespace Winshell.ShellTests;

/// <summary>
/// The shell of this test process is a Winshell instance, in a local folder of its own: it is listed, it logs, it has
/// its folders, and the control pipe reaches what it registered.
/// </summary>
public sealed class ShellInstanceTests
{
    private static async Task<InstanceInfo> Own()
    {
        await ShellFixture.Current();
        return Instances.List(false).Single(i => i.Info.Pid == Environment.ProcessId).Info;
    }

    private static async Task<List<IpcResponse>> Ask(IpcRequest request)
    {
        var info = await Own();
        var responses = new List<IpcResponse>();
        await foreach (var response in IpcServer.Request(info.Pipe, request, TestContext.Current.CancellationToken))
        {
            responses.Add(response);
            break;
        }
        return responses;
    }

    [Fact]
    public async Task A_running_shell_is_listed_under_its_id_and_program()
    {
        var info = await Own();

        Assert.Equal("winshell-tests", info.ShellId);
        Assert.Equal(Environment.ProcessPath, info.ConfigPath);
        Assert.StartsWith("winshell-", info.Pipe, StringComparison.Ordinal);
        Assert.Null(info.ExitedAt);
    }

    [Fact]
    public async Task What_the_shell_logs_goes_to_its_instance_log_with_secrets_taken_out()
    {
        var info = await Own();
        var shell = await ShellFixture.Current();

        shell.Log.Warn("test", "sync --password SENTINEL-PASSWORD");

        using var stream = new FileStream(info.Log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("WARN test: sync --password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-PASSWORD", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_folders_follow_the_shell_id_and_exist_once_read()
    {
        var shell = await ShellFixture.Current();

        Assert.Equal(Path.Combine(ShellLocations.DataRoot, "by-shell", "winshell-tests", "a.txt"), shell.DataPath("a.txt"));
        Assert.Equal(Path.Combine(ShellLocations.StateRoot, "by-shell", "winshell-tests", "a.txt"), shell.StatePath("a.txt"));
        Assert.Equal(Path.Combine(ShellLocations.CacheRoot, "by-shell", "winshell-tests", "a.txt"), shell.CachePath("a.txt"));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "a.txt"), shell.ConfigPath("a.txt"));
        Assert.True(Directory.Exists(shell.DataDir));
    }

    [Fact]
    public async Task The_control_pipe_reaches_what_the_shell_registered()
    {
        var shell = await ShellFixture.Current();
        using var handler = await ShellFixture.On(s => s.Ipc.Handle("tests", "twice", (int number) => number * 2));
        using var property = await ShellFixture.On(s => s.Ipc.Property("tests", "name", () => s.ShellId));

        var shown = Assert.Single(await Ask(new IpcRequest("show")));
        var called = Assert.Single(await Ask(new IpcRequest("call", "tests", "twice", ["21"])));
        var read = Assert.Single(await Ask(new IpcRequest("prop", "tests", "name")));
        var refused = Assert.Single(await Ask(new IpcRequest("call", "tests", "twice", ["many"])));

        Assert.Contains("function twice(number: int): int", shown.Value, StringComparison.Ordinal);
        Assert.Equal("42", called.Value);
        Assert.Equal(shell.ShellId, read.Value);
        Assert.False(refused.Ok);
        Assert.Equal("Argument number is not an int: many", refused.Error);
    }

    [Fact]
    public async Task A_handler_that_throws_fails_the_call_and_the_shell_goes_on()
    {
        using var handler = await ShellFixture.On(s => s.Ipc.Handle("tests", "fail", new Action(() => throw new InvalidOperationException("no"))));

        var failed = Assert.Single(await Ask(new IpcRequest("call", "tests", "fail", [])));
        var shown = Assert.Single(await Ask(new IpcRequest("show")));

        Assert.False(failed.Ok);
        Assert.Equal("ipc: InvalidOperationException: no", failed.Error);
        Assert.True(shown.Ok);
    }

    [Fact]
    public async Task Work_that_fails_on_the_shell_thread_writes_a_report_and_the_shell_goes_on()
    {
        var shell = await ShellFixture.Current();

        shell.Loop.Post(() => throw new InvalidOperationException("SENTINEL posted failure"));

        await Wait.UntilAsync(
            () => Directory.Exists(ShellLocations.CrashRoot)
                && Directory.GetFiles(ShellLocations.CrashRoot).Any(f => File.ReadAllText(f).Contains("SENTINEL posted failure", StringComparison.Ordinal)),
            "a crash report for the posted failure");
        Assert.True(await ShellFixture.On(_ => true));
    }
}
