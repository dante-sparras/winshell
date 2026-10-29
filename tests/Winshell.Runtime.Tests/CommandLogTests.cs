using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// A command line can carry a secret, so a process that fails to start is logged by its program alone, and a secret
/// given to a flag is taken out of the crash report like one given with <c>=</c>.
/// </summary>
public sealed class CommandLogTests : IDisposable
{
    private const string Missing = "winshell-test-missing-program.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-crash-tests", Guid.NewGuid().ToString("N"));

    public CommandLogTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void A_detached_start_that_fails_logs_the_program_but_not_its_arguments()
    {
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };

        ProcessLauncher.StartDetached(new ProcessSpec([Missing, "--password", "hunter2"], new Dictionary<string, string?>(), false, null), log);

        var message = Assert.Single(messages);
        Assert.StartsWith($"process: Failed to start {Missing}: ", message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_process_that_fails_to_start_logs_the_program_but_not_its_arguments()
    {
        using var shell = new TestShell(("shell.qml", $$"""
            import Winshell
            import Winshell.Io
            Scope {
              Process { command: ["{{Missing}}", "--password", "hunter2"]; running: true }
            }
            """));

        shell.Load();
        shell.Loop.Process();

        Assert.Contains(shell.Messages, m => m.Contains($"process: Failed to start process {Missing}: ", StringComparison.Ordinal));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_secret_given_to_a_flag_is_taken_out_of_the_crash_report()
    {
        var log = Path.Combine(_root, "log.txt");
        File.WriteAllLines(log,
        [
            "12:00:00.000  WARN process: Failed to start sync.exe --password SENTINEL-1 --verbose",
            "12:00:01.000  WARN process: Failed to start tool.exe -token SENTINEL-2 /pwd \"SENTINEL 3\" --basic-auth SENTINEL-4",
        ]);
        var crashes = new CrashReports(Path.Combine(_root, "crashes")) { LogPath = log };

        var text = File.ReadAllText(crashes.Write("a window", new InvalidOperationException("x"))!.Path);

        Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
        Assert.Contains("Failed to start sync.exe --password [redacted] --verbose", text, StringComparison.Ordinal);
        Assert.Contains("Failed to start tool.exe -token [redacted] /pwd [redacted] --basic-auth [redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_that_names_a_secret_without_a_flag_keeps_the_next_word()
    {
        const string text = "The token expired, enter your password again. Basic auth failed for /users/token.json and C:/x/secret files.";

        Assert.Equal(text, CrashRedaction.Redact(text));
    }
}
