using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Winshell.Services.Tests;

/// <summary>The life of an instance, with the control pipe and its protocol, and no shell behind it.</summary>
[Collection(InstanceRunCollection.Name)]
public sealed class ShellInstanceTests
{
    private const string Program = @"C:\shells\mine.exe";

    private static List<(InstanceInfo Info, bool Alive)> Rows(ShellInstance instance)
    {
        return Instances.List(true).Where(i => i.Info.Id == instance.Id).ToList();
    }

    private static async Task<IpcResponse> Ask(string pipe, IpcRequest request)
    {
        await foreach (var reply in IpcClient.Request(pipe, request, TestContext.Current.CancellationToken))
        {
            if (reply.Failure is { } failure)
                throw new InvalidOperationException(failure.Reason);
            return reply.Response!;
        }

        throw new InvalidOperationException("The server closed the pipe without answering.");
    }

    /// <summary>Sends one raw line, as a client from another version would, and reads the answer.</summary>
    private static async Task<string?> AskRaw(string pipe, string line)
    {
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(client, Encoding.UTF8);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(line);
        return await reader.ReadLineAsync(TestContext.Current.CancellationToken);
    }

    private static (string Pipe, EventQueue Loop, IpcServer Server) Serve(IInstanceControl control)
    {
        var pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
        var loop = new EventQueue();
        return (pipe, loop, new IpcServer(IpcServer.OpenFirst(pipe), pipe, loop, Log.Default, control));
    }

    /// <summary>Runs posted work on a thread of the test's own while the client waits for its answer.</summary>
    private static async Task<T> Pumping<T>(EventQueue loop, Func<Task<T>> ask)
    {
        var answer = ask();
        await Wait.UntilAsync(() => answer.IsCompleted, "the answer", () => loop.RunDue());
        return await answer;
    }

    [Fact]
    public void A_published_instance_is_listed_and_a_clean_close_marks_it_exited()
    {
        var instance = ShellInstance.Open("mine", Program, new Log());
        Assert.Null(instance.OpenPipe());
        instance.Publish();

        var row = Assert.Single(Rows(instance));
        Assert.True(row.Alive);
        Assert.Equal(("mine", Program, instance.Pipe), (row.Info.ShellId, row.Info.ConfigPath, row.Info.Pipe));
        Assert.Equal(Environment.ProcessId, row.Info.Pid);

        instance.Close();

        row = Assert.Single(Rows(instance));
        Assert.False(row.Alive);
        Assert.NotNull(row.Info.ExitedAt);
        // The pipe closed with it, so the name is free again.
        IpcServer.OpenFirst(instance.Pipe).Dispose();
    }

    [Fact]
    public void A_listed_instance_has_its_log_on_disk()
    {
        var log = new Log();
        var instance = ShellInstance.Open("mine", Program, log);
        Assert.Null(instance.OpenPipe());
        log.Info("shell", "Shell ID: \"mine\"");

        instance.Publish();

        using var reader = new StreamReader(new FileStream(instance.LogPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete));
        Assert.Contains("Shell ID: \"mine\"", reader.ReadToEnd(), StringComparison.Ordinal);
        instance.Close();
    }

    [Fact]
    public void A_start_that_is_abandoned_leaves_nothing_behind()
    {
        var instance = ShellInstance.Open("mine", Program, new Log());
        Assert.Null(instance.OpenPipe());

        instance.Abandon();

        Assert.False(Directory.Exists(Instances.DirOf(instance.Id)));
        Assert.Empty(Rows(instance));
        // The name is free again.
        IpcServer.OpenFirst(instance.Pipe).Dispose();
    }

    [Fact]
    public void A_start_whose_name_is_taken_listens_on_another()
    {
        var instance = ShellInstance.Open("mine", Program, new Log());
        using var held = IpcServer.OpenFirst(instance.Pipe);
        var taken = instance.Pipe;

        Assert.Null(instance.OpenPipe());

        Assert.NotEqual(taken, instance.Pipe);
        instance.Close();
    }

    [Fact]
    public void A_secret_in_a_log_line_is_taken_out_of_the_file()
    {
        var log = new Log();
        var instance = ShellInstance.Open("mine", Program, log);
        log.Warn("shell", "sync --password SENTINEL-PASSWORD");
        string text;
        using (var stream = new FileStream(instance.LogPath, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        instance.Abandon();

        Assert.Contains("sync --password [redacted]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crash_writes_one_fatal_report_that_names_the_shell_and_keeps_the_next_start_from_calling_it_clean()
    {
        var instance = ShellInstance.Open("mine", Program, new Log());
        var error = new InvalidOperationException("SENTINEL crash");
        var before = File.Exists(ShellInstance.ExitState) ? File.ReadAllText(ShellInstance.ExitState) : null;

        Assert.False(instance.ReportFatal(error));
        Assert.False(instance.ReportFatal(error));
        instance.MarkCleanExit();
        instance.Close();

        var reports = Directory.GetFiles(ShellLocations.CrashRoot).Select(File.ReadAllText)
            .Where(r => r.Contains("SENTINEL crash", StringComparison.Ordinal)).ToList();
        var report = Assert.Single(reports);
        Assert.Contains("Winshell closed: yes", report, StringComparison.Ordinal);
        Assert.Contains("Shell id: mine" + Environment.NewLine, report, StringComparison.Ordinal);
        Assert.Contains($"Config: {Program}" + Environment.NewLine, report, StringComparison.Ordinal);
        Assert.Equal(before, File.Exists(ShellInstance.ExitState) ? File.ReadAllText(ShellInstance.ExitState) : null);
    }

    [Fact]
    public async Task A_kill_reaches_a_shell_whatever_its_program_is_called()
    {
        // The server here is this test program, which the client now trusts by owner and integrity alone.
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop, () => Ask(pipe, IpcRequest.ForKill()));

        Assert.True(response.Ok);
        await Wait.UntilAsync(() => control.Quits == 1, "the quit", () => loop.RunDue());
    }

    [Fact]
    public async Task A_reload_that_worked_runs_what_follows_once_the_client_has_its_answer()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop, () => Ask(pipe, IpcRequest.ForReload()));

        Assert.True(response.Ok);
        await Wait.UntilAsync(() => control.Answered == 1, "what follows the reload", () => loop.RunDue());
    }

    [Fact]
    public async Task A_reload_that_failed_says_why_and_the_shell_stays()
    {
        var control = new FakeInstanceControl { ReloadError = "It did not start." };
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop, () => Ask(pipe, IpcRequest.ForReload()));

        Assert.Equal((false, "It did not start."), (response.Ok, response.Error));
        loop.RunDue();
        Assert.Equal(0, control.Answered);
    }

    [Fact]
    public async Task A_build_error_is_handed_to_the_shell_with_its_project()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop,
            () => Ask(pipe, IpcRequest.ForBuild("Bar.cs(1,1): error CS1002: ; expected", @"C:\mine\Bar.csproj")));
        var refused = await Pumping(loop, () => Ask(pipe, new IpcRequest(IpcOps.Build, Args: ["only one"])));

        Assert.True(response.Ok);
        Assert.Equal(("Bar.cs(1,1): error CS1002: ; expected", @"C:\mine\Bar.csproj"),
            (control.BuildError, control.BuildProject));
        Assert.Equal((false, "build takes the error and the project file."), (refused.Ok, refused.Error));
        Assert.Equal(0, control.Quits);
    }

    [Fact]
    public async Task What_throws_on_the_shell_thread_is_a_failed_response_whichever_request_it_was()
    {
        var control = new FakeInstanceControl { InspectFailure = new InvalidOperationException("no screen") };
        control.Ipc.Handle("tests", "fail", new Action(() => throw new InvalidOperationException("no")));
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var called = await Pumping(loop, () => Ask(pipe, IpcRequest.ForCall("tests", "fail", [])));
        var inspected = await Pumping(loop, () => Ask(pipe, IpcRequest.ForInspect("tree", [])));
        var listened = await Pumping(loop, () => Ask(pipe, IpcRequest.ForListen("tests", "gone")));

        Assert.Equal((false, "ipc: InvalidOperationException: no"), (called.Ok, called.Error));
        Assert.Equal((false, "ipc: InvalidOperationException: no screen"), (inspected.Ok, inspected.Error));
        Assert.Equal((false, "Signal not found: tests.gone."), (listened.Ok, listened.Error));
    }

    [Fact]
    public async Task A_client_with_no_protocol_number_is_served_and_reads_past_the_shells()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var line = await Pumping(loop, () => AskRaw(pipe, """{"op":"show"}"""));

        var response = JsonSerializer.Deserialize(line!, IpcJson.Default.IpcResponse)!;
        Assert.True(response.Ok);
        Assert.Equal(IpcProtocol.Current, response.Protocol);
    }

    [Fact]
    public async Task A_client_from_a_newer_protocol_is_refused_in_a_plain_failed_response()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var line = await AskRaw(pipe, """{"op":"kill","protocol":99}""");

        var response = JsonSerializer.Deserialize(line!, IpcJson.Default.IpcResponse)!;
        Assert.False(response.Ok);
        Assert.Equal(IpcProtocol.ClientRefusal(99), response.Error);
        Assert.Contains("this winshell speaks 99", response.Error, StringComparison.Ordinal);
        loop.RunDue();
        Assert.Equal(0, control.Quits);
    }

    [Fact]
    public async Task A_shell_with_no_protocol_number_still_answers_the_client()
    {
        var pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
        await using var shell = IpcServer.OpenFirst(pipe);
        var answering = AnswerOnce(shell, """{"ok":true,"value":"old","error":null}""");

        var response = await Ask(pipe, IpcRequest.ForShow(false));

        Assert.Equal((true, "old"), (response.Ok, response.Value));
        var request = JsonSerializer.Deserialize(await answering, IpcJson.Default.IpcRequest)!;
        Assert.Equal(IpcProtocol.Current, request.Protocol);
        Assert.Equal(ProductVersion.Current.Text, request.Winshell);
    }

    [Fact]
    public async Task A_client_of_another_release_is_refused_before_the_shell_acts()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;
        var other = ProductVersion.Current.Major == 9 ? "1.0.0" : "9.9.9";

        var line = await AskRaw(pipe, $$"""{"op":"reload","winshell":"{{other}}"}""");

        var response = JsonSerializer.Deserialize(line!, IpcJson.Default.IpcResponse)!;
        Assert.False(response.Ok);
        Assert.Equal(IpcProtocol.ReleaseRefusal(ProductVersion.Current.Text, other), response.Error);
        Assert.Contains("Use the winshell that matches", response.Error, StringComparison.Ordinal);
        loop.RunDue();
        Assert.Equal(0, control.Reloads);
    }

    [Fact]
    public async Task A_client_of_another_release_can_still_stop_the_shell()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;
        var other = ProductVersion.Current.Major == 9 ? "1.0.0" : "9.9.9";

        var line = await Pumping(loop, () => AskRaw(pipe, $$"""{"op":"kill","winshell":"{{other}}"}"""));

        Assert.True(JsonSerializer.Deserialize(line!, IpcJson.Default.IpcResponse)!.Ok);
        loop.RunDue();
        Assert.Equal(1, control.Quits);
    }

    [Fact]
    public async Task A_shell_of_another_release_can_still_be_stopped_from_here()
    {
        var pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
        await using var shell = IpcServer.OpenFirst(pipe);
        var other = ProductVersion.Current.Major == 9 ? "1.0.0" : "9.9.9";
        var answering = AnswerOnce(shell, $$"""{"ok":true,"winshell":"{{other}}"}""");

        await Ask(pipe, IpcRequest.ForKill());

        // Kill must reach a shell of any release, so the line is pinned in full: an older shell has to read every field of it.
        var version = JsonSerializer.Serialize(ProductVersion.Current.Text);
        Assert.Equal(
            $$"""{"op":"kill","target":null,"name":null,"args":null,"protocol":{{IpcProtocol.Current}},"winshell":{{version}}}""",
            await answering);
    }

    [Fact]
    public async Task A_client_of_this_release_is_served_and_one_with_no_release_still_is()
    {
        var control = new FakeInstanceControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;
        var version = JsonSerializer.Serialize(ProductVersion.Current.Text);

        var same = await Pumping(loop, () => AskRaw(pipe, $$"""{"op":"show","winshell":{{version}}}"""));
        var named = JsonSerializer.Deserialize(same!, IpcJson.Default.IpcResponse)!;
        Assert.True(named.Ok);
        Assert.Equal(ProductVersion.Current.Text, named.Winshell);

        var old = await Pumping(loop, () => AskRaw(pipe, """{"op":"show"}"""));
        Assert.True(JsonSerializer.Deserialize(old!, IpcJson.Default.IpcResponse)!.Ok);
    }

    [Fact]
    public async Task A_shell_of_another_release_is_refused_with_a_plain_message()
    {
        var pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
        await using var shell = IpcServer.OpenFirst(pipe);
        var other = ProductVersion.Current.Major == 9 ? "1.0.0" : "9.9.9";
        var answering = AnswerOnce(shell, $$"""{"ok":true,"winshell":"{{other}}"}""");

        IpcReply reply = default;
        await foreach (var next in IpcClient.Request(pipe, IpcRequest.ForShow(false),
                           TestContext.Current.CancellationToken))
        {
            reply = next;
            break;
        }

        Assert.NotNull(reply.Failure);
        Assert.Equal(IpcProtocol.ReleaseRefusal(other, ProductVersion.Current.Text), reply.Failure.Value.Reason);
        Assert.True(reply.Failure.Value.Rejected);
        await answering;
    }

    private static async Task<string> AnswerOnce(NamedPipeServerStream shell, string answer)
    {
        await shell.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        var reader = new StreamReader(shell, Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(shell, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync(answer);
        return line!;
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, "This shell speaks control protocol 1 and this winshell speaks 2.")]
    [InlineData(-1, "This shell speaks control protocol 1 and this winshell speaks -1.")]
    public void A_shell_refuses_a_client_outside_what_it_speaks(int client, string? refusal)
    {
        AssertStarts(refusal, IpcProtocol.ClientRefusal(client));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(5, null)]
    [InlineData(-1, "This shell speaks control protocol -1 and this winshell speaks 1.")]
    public void A_client_refuses_only_a_shell_older_than_it_speaks(int shell, string? refusal)
    {
        AssertStarts(refusal, IpcProtocol.ShellRefusal(shell));
    }

    [Theory]
    [InlineData(null, "1.2.3", null)]
    [InlineData("1.2.3", "", null)]
    [InlineData("1.2.3-dev", "1.2.3+abc", null)]
    [InlineData("1.2.3", "1.2.4", "This shell is Winshell 1.2.3 and this winshell is 1.2.4.")]
    [InlineData("2.0.0", "1.9.9", "This shell is Winshell 2.0.0 and this winshell is 1.9.9.")]
    public void A_different_release_is_refused_and_a_missing_one_is_not(string? shell, string? client, string? refusal)
    {
        AssertStarts(refusal, IpcProtocol.ReleaseRefusal(shell, client));
    }

    private static void AssertStarts(string? expected, string? actual)
    {
        if (expected is null)
            Assert.Null(actual);
        else
            Assert.StartsWith(expected, actual, StringComparison.Ordinal);
    }
}
