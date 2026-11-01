using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>FileView.waitForJob blocks until the in-flight load or save has raised its signal.</summary>
public class FileViewWaitForJobTests
{
    /// <summary>Fails a wait that never gets its signal. The wait itself is the signal, not this ceiling.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(5);

    private const string Qml = """
        import Winshell.Io
        FileView { preload: false; printErrors: false }
        """;

    [Fact]
    public void Nothing_running_returns_false_and_a_finished_load_is_readable()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var view = (FileView)shell.Load();
        var order = new List<string>();
        view.Connect("loaded", _ =>
        {
            order.Add("loaded");
            shell.Loop.Post(() => order.Add("late"));
        });
        Assert.Equal(false, shell.Eval(view, "waitForJob()"));

        var path = Path.Combine(shell.Directory, "note.txt");
        File.WriteAllText(path, "hello");
        shell.Loop.Post(() => order.Add("early"));
        view.Path = path;
        view.Reload();

        Assert.Equal(true, shell.Eval(view, "waitForJob()"));
        Assert.Equal(new[] { "early", "loaded" }, order);
        Assert.Equal("hello", shell.Eval(view, "text()"));
        Assert.Equal(false, shell.Eval(view, "waitForJob()"));
        shell.Loop.Process();
        Assert.Equal(new[] { "early", "loaded", "late" }, order);
    }

    [Fact]
    public void A_load_that_already_finished_on_this_thread_is_not_a_job()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var view = (FileView)shell.Load();
        var path = Path.Combine(shell.Directory, "note.txt");
        File.WriteAllText(path, "hello");
        view.BlockLoading = true;
        view.Preload = true;
        view.Path = path;
        Assert.Equal("hello", view.GetText());
        Assert.Equal(false, shell.Eval(view, "waitForJob()"));
    }

    [Fact]
    public void A_failed_load_and_a_failed_save_raise_their_signals_before_returning()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var view = (FileView)shell.Load();
        var order = new List<string>();
        view.Connect("loadFailed", args => order.Add("loadFailed:" + args[0]));
        view.Connect("saveFailed", args => order.Add("saveFailed:" + args[0]));

        view.Path = Path.Combine(shell.Directory, "missing.txt");
        view.Reload();
        Assert.Equal(true, shell.Eval(view, "waitForJob()"));
        Assert.Equal(new[] { "loadFailed:" + (int)FileView.FileViewError.FileNotFound }, order);

        view.Path = shell.Directory;
        view.SetText("nope");
        Assert.Equal(true, shell.Eval(view, "waitForJob()"));
        Assert.Equal(
            new[]
            {
                "loadFailed:" + (int)FileView.FileViewError.FileNotFound,
                "saveFailed:" + (int)FileView.FileViewError.NotAFile,
            },
            order);
        Assert.Equal(false, shell.Eval(view, "waitForJob()"));
    }

    [Fact]
    public void A_queued_save_finishes_after_the_one_in_progress()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var view = (FileView)shell.Load();
        var path = Path.Combine(shell.Directory, "note.txt");
        view.Path = path;
        var order = new List<string>();
        view.Connect("saved", _ => order.Add("saved"));

        using var entered = new ManualResetEventSlim(false);
        using var secondQueued = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var held = false;
        view.SetJobGateForTests(_ =>
        {
            if (held)
                return;
            held = true;
            entered.Set();
            release.Wait(Hang);
        });
        var releaser = new Thread(() =>
        {
            if (!entered.Wait(Hang) || !secondQueued.Wait(Hang))
                return;
            release.Set();
        })
        { IsBackground = true };
        try
        {
            releaser.Start();
            view.SetText("one");
            Assert.True(entered.Wait(Hang), "The save never reached the gate.");
            view.SetText("two");
            secondQueued.Set();
            Assert.Equal(true, shell.Eval(view, "waitForJob()"));
            Assert.Equal(new[] { "saved", "saved" }, order);
            Assert.Equal("two", File.ReadAllText(path));
            Assert.Equal("two", shell.Eval(view, "text()"));
            shell.Loop.Process();
            Assert.Equal(new[] { "saved", "saved" }, order);
        }
        finally
        {
            release.Set();
            secondQueued.Set();
            Assert.True(releaser.Join(Hang), "The releaser did not finish.");
            view.SetJobGateForTests(null);
            Assert.True(view.JoinJobsForTests(Hang), "The file job did not finish.");
        }
    }

    [Fact]
    public void A_job_whose_completion_needs_the_loop_does_not_deadlock()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var view = (FileView)shell.Load();
        var path = Path.Combine(shell.Directory, "note.txt");
        File.WriteAllText(path, "hello");
        var order = new List<string>();
        view.Connect("loaded", _ => order.Add("loaded"));

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var pumped = 0;
        // The read thread posts here and waits until this loop runs it. waitForJob is on that same
        // thread, so a blocking wait that does not run posted work never returns. Hang is only the guard.
        view.SetJobGateForTests(loop =>
        {
            entered.Set();
            using var ran = new ManualResetEventSlim(false);
            loop.Post(() => ran.Set());
            if (ran.Wait(Hang))
                Interlocked.Exchange(ref pumped, 1);
            release.Wait(Hang);
        });
        var releaser = new Thread(() =>
        {
            if (entered.Wait(Hang))
                release.Set();
        })
        { IsBackground = true };
        try
        {
            releaser.Start();
            view.Path = path;
            view.Reload();
            Assert.Equal(true, shell.Eval(view, "waitForJob()"));
            Assert.Equal(1, Volatile.Read(ref pumped));
            Assert.Equal(new[] { "loaded" }, order);
            Assert.Equal("hello", shell.Eval(view, "text()"));
            shell.Loop.Process();
            Assert.Equal(new[] { "loaded" }, order);
        }
        finally
        {
            release.Set();
            Assert.True(releaser.Join(Hang), "The releaser did not finish.");
            view.SetJobGateForTests(null);
            Assert.True(view.JoinJobsForTests(Hang), "The file job did not finish.");
        }
    }

    [Fact]
    public void Destroying_the_view_while_it_waits_returns_false_without_the_signal()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var view = (FileView)shell.Load();
        var path = Path.Combine(shell.Directory, "note.txt");
        File.WriteAllText(path, "hello");
        var signals = 0;
        view.Connect("loaded", _ => signals++);
        view.Connect("loadFailed", _ => signals++);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        view.SetJobGateForTests(loop =>
        {
            entered.Set();
            loop.Post(() => view.Destroy());
            release.Wait(Hang);
        });
        var releaser = new Thread(() =>
        {
            if (entered.Wait(Hang))
                release.Set();
        })
        { IsBackground = true };
        try
        {
            releaser.Start();
            view.Path = path;
            view.Reload();
            Assert.False(view.WaitForJob());
            Assert.Equal(0, signals);
        }
        finally
        {
            release.Set();
            Assert.True(releaser.Join(Hang), "The releaser did not finish.");
            view.SetJobGateForTests(null);
            Assert.True(view.JoinJobsForTests(Hang), "The file job did not finish.");
            shell.Loop.Process();
            Assert.Equal(0, signals);
        }
    }
}
