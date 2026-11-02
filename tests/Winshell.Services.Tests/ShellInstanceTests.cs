using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Winshell.Services.Tests;

/// <summary>The life of an instance, with the control pipe and its protocol, and no shell behind it.</summary>
public sealed class ShellInstanceTests
{
    private const string Program = @"C:\shells\mine.exe";

    private sealed class FakeControl : IInstanceControl
    {
        public string? ReloadError { get; init; }

        public int Reloads { get; private set; }

        public int Answered { get; private set; }

        public int Quits { get; private set; }

        public IIpcTargets Ipc { get; } = new ShellIpc(new Log());

        public string? Reload(bool hard)
        {
            Reloads++;
            return ReloadError;
        }

        public void Reloaded() => Answered++;

        public void Quit() => Quits++;
    }

    private static List<(InstanceInfo Info, bool Alive)> Rows(ShellInstance instance) =>
        Instances.List(true).Where(i => i.Info.Id == instance.Id).ToList();

    private static async Task<IpcResponse> Ask(string pipe, IpcRequest request)
    {
        await foreach (var response in IpcServer.Request(pipe, request, TestContext.Current.CancellationToken))
            return response;
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
        return (pipe, loop, new IpcServer(IpcServer.OpenFirst(pipe), pipe, loop, control));
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
        var instance = ShellInstance.Open(Program, new Log());
        Assert.True(instance.TryOpenPipe());
        instance.Publish("mine");

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
    public void A_start_that_is_abandoned_leaves_nothing_behind()
    {
        var instance = ShellInstance.Open(Program, new Log());
        Assert.True(instance.TryOpenPipe());

        instance.Abandon();

        Assert.False(Directory.Exists(Instances.DirOf(instance.Id)));
        Assert.Empty(Rows(instance));
        // The name is free again.
        IpcServer.OpenFirst(instance.Pipe).Dispose();
    }

    [Fact]
    public void A_start_whose_name_is_taken_listens_on_another()
    {
        var instance = ShellInstance.Open(Program, new Log());
        using var held = IpcServer.OpenFirst(instance.Pipe);
        var taken = instance.Pipe;

        Assert.True(instance.TryOpenPipe());

        Assert.NotEqual(taken, instance.Pipe);
        instance.Close();
    }

    [Fact]
    public void A_crash_writes_one_fatal_report_and_keeps_the_next_start_from_calling_it_clean()
    {
        var instance = ShellInstance.Open(Program, new Log());
        var error = new InvalidOperationException("SENTINEL crash");
        var before = File.Exists(ShellInstance.ExitState) ? File.ReadAllText(ShellInstance.ExitState) : null;

        Assert.False(instance.ReportFatal(error));
        Assert.False(instance.ReportFatal(error));
        instance.MarkCleanExit();
        instance.Close();

        var reports = Directory.GetFiles(ShellLocations.CrashRoot).Select(File.ReadAllText).Where(r => r.Contains("SENTINEL crash", StringComparison.Ordinal)).ToList();
        Assert.Contains("Winshell closed: yes", Assert.Single(reports), StringComparison.Ordinal);
        Assert.Equal(before, File.Exists(ShellInstance.ExitState) ? File.ReadAllText(ShellInstance.ExitState) : null);
    }

    [Fact]
    public async Task A_kill_reaches_a_shell_whatever_its_program_is_called()
    {
        // The server here is this test program, which the client now trusts by owner and integrity alone.
        var control = new FakeControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop, () => Ask(pipe, new IpcRequest("kill")));

        Assert.True(response.Ok);
        await Wait.UntilAsync(() => control.Quits == 1, "the quit", () => loop.RunDue());
    }

    [Fact]
    public async Task A_reload_that_worked_runs_what_follows_once_the_client_has_its_answer()
    {
        var control = new FakeControl();
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop, () => Ask(pipe, new IpcRequest("reload")));

        Assert.True(response.Ok);
        await Wait.UntilAsync(() => control.Answered == 1, "what follows the reload", () => loop.RunDue());
    }

    [Fact]
    public async Task A_reload_that_failed_says_why_and_the_shell_stays()
    {
        var control = new FakeControl { ReloadError = "It did not start." };
        var (pipe, loop, server) = Serve(control);
        using var _ = server;

        var response = await Pumping(loop, () => Ask(pipe, new IpcRequest("reload")));

        Assert.Equal((false, "It did not start."), (response.Ok, response.Error));
        loop.RunDue();
        Assert.Equal(0, control.Answered);
    }

    [Fact]
    public async Task A_client_with_no_protocol_number_is_served_and_reads_past_the_shells()
    {
        var control = new FakeControl();
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
        var control = new FakeControl();
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

        var response = await Ask(pipe, new IpcRequest("show"));

        Assert.Equal((true, "old"), (response.Ok, response.Value));
        var request = JsonSerializer.Deserialize(await answering, IpcJson.Default.IpcRequest)!;
        Assert.Equal(IpcProtocol.Current, request.Protocol);
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
    [InlineData(0, 0, 1, null)]
    [InlineData(1, 0, 1, null)]
    [InlineData(2, 0, 1, "This shell speaks control protocol 1 and this winshell speaks 2.")]
    [InlineData(1, 2, 3, "This shell speaks control protocol 3 and this winshell speaks 1.")]
    public void A_shell_refuses_a_client_outside_what_it_speaks(int client, int oldest, int current, string? refusal)
    {
        AssertStarts(refusal, IpcProtocol.ClientRefusal(client, current, oldest));
    }

    [Theory]
    [InlineData(0, 0, 1, null)]
    [InlineData(5, 0, 1, null)]
    [InlineData(1, 2, 3, "This shell speaks control protocol 1 and this winshell speaks 3.")]
    public void A_client_refuses_only_a_shell_older_than_it_speaks(int shell, int oldest, int current, string? refusal)
    {
        AssertStarts(refusal, IpcProtocol.ShellRefusal(shell, current, oldest));
    }

    private static void AssertStarts(string? expected, string? actual)
    {
        if (expected is null)
            Assert.Null(actual);
        else
            Assert.StartsWith(expected, actual, StringComparison.Ordinal);
    }
}
