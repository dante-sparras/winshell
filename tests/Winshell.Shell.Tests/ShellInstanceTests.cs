namespace Winshell.ShellTests;

/// <summary>
/// The shell of this test process is a Winshell instance, in a local folder of its own: it is listed, it logs, it has
/// its folders, and the control pipe reaches what it registered.
/// </summary>
[Trait("Category", "Slow")]
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
        await foreach (var response in IpcClient.Request(info.Pipe, request, TestContext.Current.CancellationToken))
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

        using var stream = new FileStream(info.Log, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("WARN test: sync --password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-PASSWORD", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_folders_follow_the_shell_id_and_exist_once_read()
    {
        var shell = await ShellFixture.Current();

        Assert.Equal(Path.Combine(ShellLocations.DataRoot, "by-shell", "winshell-tests", "a.txt"),
            shell.DataPath("a.txt"));
        Assert.Equal(Path.Combine(ShellLocations.StateRoot, "by-shell", "winshell-tests", "a.txt"),
            shell.StatePath("a.txt"));
        Assert.Equal(Path.Combine(ShellLocations.CacheRoot, "by-shell", "winshell-tests", "a.txt"),
            shell.CachePath("a.txt"));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "a.txt"), shell.ConfigPath("a.txt"));
        Assert.True(Directory.Exists(shell.DataDir));
    }

    [Fact]
    public async Task The_control_pipe_reaches_what_the_shell_registered()
    {
        var shell = await ShellFixture.Current();
        using var handler = await ShellFixture.On(s => s.Ipc.Handle("tests", "twice", (int number) => number * 2));
        using var property = await ShellFixture.On(s => s.Ipc.Property("tests", "name", () => s.ShellId));

        var shown = Assert.Single(await Ask(IpcRequest.ForShow(false)));
        var called = Assert.Single(await Ask(IpcRequest.ForCall("tests", "twice", ["21"])));
        var read = Assert.Single(await Ask(IpcRequest.ForProp("tests", "name")));
        var refused = Assert.Single(await Ask(IpcRequest.ForCall("tests", "twice", ["many"])));

        Assert.Contains("function twice(number: int): int", shown.Value, StringComparison.Ordinal);
        Assert.Equal("42", called.Value);
        Assert.Equal(shell.ShellId, read.Value);
        Assert.False(refused.Ok);
        Assert.Equal("Argument number is not an int: many.", refused.Error);
    }

    [Fact]
    public async Task A_build_error_shows_on_the_running_shell()
    {
        await ShellFixture.Hold(async () =>
        {
            var shell = await ShellFixture.Current();
            var response =
                Assert.Single(await Ask(IpcRequest.ForBuild("Bar.cs(1,1): error CS1002: ; expected",
                    @"C:\mine\Bar.csproj")));

            try
            {
                Assert.True(response.Ok);
                Assert.Equal(NoticeKind.BuildError, shell.Notices!.Current!.Kind);
                Assert.Contains("CS1002", shell.Notices.Current.Body, StringComparison.Ordinal);
                Assert.Equal(Environment.ProcessId, (await Own()).Pid);
            }
            finally
            {
                // The shell is one for the whole run. A notice left up is a second window for the tests that follow.
                await DismissNotice();
            }
        });
    }

    [Fact]
    public async Task A_handler_that_throws_fails_the_call_and_the_shell_goes_on()
    {
        using var handler = await ShellFixture.On(s =>
            s.Ipc.Handle("tests", "fail", new Action(() => throw new InvalidOperationException("no"))));

        var failed = Assert.Single(await Ask(IpcRequest.ForCall("tests", "fail", [])));
        var shown = Assert.Single(await Ask(IpcRequest.ForShow(false)));

        Assert.False(failed.Ok);
        Assert.Equal("ipc: InvalidOperationException: no", failed.Error);
        Assert.True(shown.Ok);
    }

    [Fact]
    public async Task Work_that_fails_on_the_shell_thread_writes_a_report_and_the_shell_goes_on()
    {
        await ShellFixture.Hold(async () =>
        {
            var shell = await ShellFixture.Current();

            shell.Loop.Post(() => throw new InvalidOperationException("SENTINEL posted failure"));

            await Wait.UntilAsync(
                () => Directory.Exists(ShellLocations.CrashRoot)
                      && Directory.GetFiles(ShellLocations.CrashRoot).Any(f =>
                          ReadClosed(f).Contains("SENTINEL posted failure", StringComparison.Ordinal)),
                "a crash report for the posted failure");
            Assert.True(await ShellFixture.On(_ => true));
            // The report shows a notice. Take it down before another test looks at the shell's windows.
            await DismissNotice();
        });
    }

    /// <summary>Closes the shell's notice. The show is already posted by the time a report or a build error is visible.</summary>
    private static Task DismissNotice()
    {
        return ShellFixture.On(shell =>
        {
            shell.Notices?.Hide();
            return true;
        });
    }

    /// <summary>
    /// A file's text, refusing to share with a writer as <see cref="File.ReadAllText(string)"/> does, so a report still
    /// being written fails. Delete is shared: a report just renamed into the folder is still open to the rename for an
    /// instant.
    /// </summary>
    private static string ReadClosed(string path)
    {
        using var reader =
            new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        return reader.ReadToEnd();
    }
}
