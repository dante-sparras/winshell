using FluentAssertions;
using Winshell.Core.Diagnostics;

namespace Winshell.Tests;

[TestClass]
public sealed class PolledSnapshotTests
{
    [TestMethod]
    public async Task The_first_kick_starts_one_read_and_a_second_kick_waits()
    {
        var snapshot = new PolledSnapshot<string>(5_000, () => 0, "initial");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;

        snapshot.Current.Should().Be("initial");
        snapshot.Kick(async () =>
        {
            Interlocked.Increment(ref starts);
            started.TrySetResult();
            return await release.Task;
        });

        snapshot.Busy.Should().BeTrue();
        snapshot.Current.Should().Be("initial");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Kick(async () =>
        {
            Interlocked.Increment(ref starts);
            return await release.Task;
        });
        await Drain();
        starts.Should().Be(1);
        snapshot.Busy.Should().BeTrue();

        release.SetResult("only-read");
        await Until(() => !snapshot.Busy);
        await Drain();
        starts.Should().Be(1);
        snapshot.Busy.Should().BeFalse();
        snapshot.Current.Should().Be("only-read");
    }

    [TestMethod]
    public async Task A_kick_inside_the_interval_does_nothing_until_the_clock_passes_it()
    {
        long now = 10_000;
        var snapshot = new PolledSnapshot<string>(5_000, () => Volatile.Read(ref now), "initial");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        snapshot.Kick(() => release.Task);
        snapshot.Busy.Should().BeTrue();
        release.SetResult("kept-literal");
        await Until(() => !snapshot.Busy);
        snapshot.Current.Should().Be("kept-literal");

        var early = 0;
        snapshot.Kick(() =>
        {
            Interlocked.Increment(ref early);
            return Task.FromResult("too-soon");
        });
        snapshot.Busy.Should().BeFalse();
        await Drain();
        early.Should().Be(0);
        snapshot.Current.Should().Be("kept-literal");

        Volatile.Write(ref now, 10_000 + 5_000 - 1);
        snapshot.Kick(() =>
        {
            Interlocked.Increment(ref early);
            return Task.FromResult("still-early");
        });
        snapshot.Busy.Should().BeFalse();
        await Drain();
        early.Should().Be(0);
        snapshot.Current.Should().Be("kept-literal");

        Volatile.Write(ref now, 10_000 + 5_000 + 1);
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        snapshot.Kick(() =>
        {
            restarted.TrySetResult();
            return Task.FromResult("later-literal");
        });
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => !snapshot.Busy);
        snapshot.Current.Should().Be("later-literal");
        early.Should().Be(0);
    }

    [TestMethod]
    public async Task A_failed_read_keeps_the_value_and_waits_for_the_interval()
    {
        long now = 100;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        snapshot.Kick(() => release.Task);
        release.SetResult("previous-literal");
        await Until(() => snapshot.Current == "previous-literal" && !snapshot.Busy);
        snapshot.Current.Should().Be("previous-literal");

        Volatile.Write(ref now, 100 + 1_000 + 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        snapshot.Kick(async () =>
        {
            started.TrySetResult();
            await fail.Task;
            throw new InvalidOperationException("boom");
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Busy.Should().BeTrue();
        snapshot.Current.Should().Be("previous-literal");
        fail.SetResult("ignored");
        await Until(() => !snapshot.Busy);
        snapshot.Busy.Should().BeFalse();
        snapshot.Current.Should().Be("previous-literal");

        var again = 0;
        snapshot.Kick(() =>
        {
            Interlocked.Increment(ref again);
            return Task.FromResult("nope");
        });
        snapshot.Busy.Should().BeFalse();
        await Drain();
        again.Should().Be(0);
        snapshot.Current.Should().Be("previous-literal");

        Volatile.Write(ref now, 100 + 1_000 + 1 + 1_000 - 1);
        snapshot.Kick(() =>
        {
            Interlocked.Increment(ref again);
            return Task.FromResult("still-nope");
        });
        snapshot.Busy.Should().BeFalse();
        await Drain();
        again.Should().Be(0);
        snapshot.Current.Should().Be("previous-literal");

        Volatile.Write(ref now, 100 + 1_000 + 1 + 1_000 + 1);
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        snapshot.Kick(() =>
        {
            restarted.TrySetResult();
            return Task.FromResult("after-backoff");
        });
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => !snapshot.Busy);
        snapshot.Busy.Should().BeFalse();
        snapshot.Current.Should().Be("after-backoff");
        again.Should().Be(0);
    }

    [TestMethod]
    public async Task An_open_panel_reads_on_the_shorter_interval()
    {
        long now = 1_000;
        var snapshot = new PolledSnapshot<int>(2_000, 250, () => now, 0);
        await snapshot.Refresh(() => Task.FromResult(1));
        snapshot.Wait.Should().Be(2_000);

        snapshot.SetOpen(true);
        snapshot.Open.Should().BeTrue();
        snapshot.Wait.Should().Be(0);
        await snapshot.Refresh(() => Task.FromResult(2));
        snapshot.Current.Should().Be(2);
        snapshot.Wait.Should().Be(250);

        now += 100;
        snapshot.Wait.Should().Be(150);
        await snapshot.Refresh(() => Task.FromResult(3));
        snapshot.Current.Should().Be(2);

        now += 150;
        await snapshot.Refresh(() => Task.FromResult(4));
        snapshot.Current.Should().Be(4);
        snapshot.Reads.Should().Be(3);
    }

    [TestMethod]
    public async Task Opening_or_closing_throws_away_the_read_in_flight_and_reads_at_once()
    {
        var snapshot = new PolledSnapshot<string>(2_000, 250, () => 0, "initial");
        var glyph = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var landed = snapshot.Refresh(() => glyph.Task);
        snapshot.Busy.Should().BeTrue();

        snapshot.SetOpen(true);
        snapshot.Busy.Should().BeFalse();
        await snapshot.Refresh(() => Task.FromResult("mix"));
        glyph.SetResult("late glyph");
        await landed;

        snapshot.Current.Should().Be("mix");
        snapshot.Busy.Should().BeFalse();
        snapshot.Reads.Should().Be(1);
    }

    [TestMethod]
    public async Task A_merge_sees_the_value_on_hand()
    {
        var snapshot = new PolledSnapshot<string>(0, () => 0, "kept");

        await snapshot.Refresh(() => Task.FromResult("read"), (current, read) => current + "+" + read);

        snapshot.Current.Should().Be("kept+read");
    }

    [TestMethod]
    public async Task A_merge_that_throws_is_a_failed_read_and_the_next_one_runs()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");

        await snapshot.Refresh(() => Task.FromResult("read"), (_, _) => throw new InvalidOperationException("boom"));
        snapshot.Busy.Should().BeFalse();
        snapshot.Current.Should().Be("initial");
        snapshot.Reads.Should().Be(0);
        snapshot.Wait.Should().Be(1_000);

        Volatile.Write(ref now, 1_000);
        await snapshot.Refresh(() => Task.FromResult("next"));
        snapshot.Current.Should().Be("next");
    }

    [TestMethod]
    public async Task A_drop_throws_away_the_read_in_flight_and_lets_the_next_run()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(60_000, () => now, "initial");
        await snapshot.Refresh(() => Task.FromResult("old place"));
        var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        now = 60_000;
        var landed = snapshot.Refresh(() => slow.Task);

        snapshot.Drop();
        snapshot.Assign("loading");
        await snapshot.Refresh(() => Task.FromResult("new place"));
        slow.SetResult("old place again");
        await landed;

        snapshot.Current.Should().Be("new place");
    }

    [TestMethod]
    public void The_latest_read_to_start_wins()
    {
        var snapshot = new PolledSnapshot<string>(30_000, () => 0, "initial");

        var first = snapshot.Begin();
        var second = snapshot.Begin();

        snapshot.Latest(first).Should().BeFalse();
        snapshot.Store(first, "older").Should().BeFalse();
        snapshot.Store(second, "newer").Should().BeTrue();
        snapshot.Store(second, "and its full read", (current, read) => current + ", " + read).Should().BeTrue();
        snapshot.Current.Should().Be("newer, and its full read");
        snapshot.Reads.Should().Be(2);
    }

    [TestMethod]
    public async Task A_read_begun_outside_the_pace_drops_the_paced_one_and_holds_the_interval()
    {
        var snapshot = new PolledSnapshot<string>(30_000, () => 0, "initial");
        var glyph = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var landed = snapshot.Refresh(() => glyph.Task);

        var load = snapshot.Begin();
        snapshot.Busy.Should().BeFalse();
        snapshot.Wait.Should().Be(30_000);
        glyph.SetResult("glyph");
        await landed;
        snapshot.Store(load, "full").Should().BeTrue();

        snapshot.Current.Should().Be("full");
    }

    [TestMethod]
    public async Task An_equal_value_keeps_the_object_on_hand_and_says_nothing()
    {
        long now = 0;
        var first = new Reading(4);
        var snapshot = new PolledSnapshot<Reading>(1_000, () => Volatile.Read(ref now), first);
        var changes = 0;
        snapshot.Changed += () => Interlocked.Increment(ref changes);

        await snapshot.Refresh(() => Task.FromResult(new Reading(4)));
        snapshot.Current.Should().BeSameAs(first);
        snapshot.Reads.Should().Be(1);
        Volatile.Read(ref changes).Should().Be(0);

        Volatile.Write(ref now, 1_000);
        await snapshot.Refresh(() => Task.FromResult(new Reading(5)));
        snapshot.Current.Should().Be(new Reading(5));
        Volatile.Read(ref changes).Should().Be(1);

        snapshot.Assign(new Reading(5));
        snapshot.Assign(reading => reading with { Level = 6 });
        snapshot.Store(snapshot.Begin(), new Reading(6)).Should().BeTrue();
        Volatile.Read(ref changes).Should().Be(2);
    }

    [TestMethod]
    public async Task A_read_that_keeps_failing_waits_longer_each_time_until_one_succeeds()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");
        Func<Task<string>> fail = () => Task.FromException<string>(new InvalidOperationException("boom"));

        await snapshot.Refresh(fail);
        snapshot.Wait.Should().Be(1_000);
        Volatile.Write(ref now, 1_000);
        await snapshot.Refresh(fail);
        snapshot.Wait.Should().Be(2_000);
        Volatile.Write(ref now, 3_000);
        await snapshot.Refresh(fail);
        snapshot.Wait.Should().Be(4_000);
        for (var i = 0; i < 8; i++)
        {
            Volatile.Write(ref now, Volatile.Read(ref now) + 60_000);
            await snapshot.Refresh(fail);
        }

        snapshot.Wait.Should().Be(60_000);
        snapshot.Expire();
        await snapshot.Refresh(fail);
        snapshot.Wait.Should().Be(1_000);

        Volatile.Write(ref now, Volatile.Read(ref now) + 1_000);
        await snapshot.Refresh(fail);
        snapshot.Wait.Should().Be(2_000);
        Volatile.Write(ref now, Volatile.Read(ref now) + 2_000);
        await snapshot.Refresh(() => Task.FromResult("back"));
        snapshot.Current.Should().Be("back");
        snapshot.Wait.Should().Be(1_000);
    }

    [TestMethod]
    public async Task A_read_that_never_completes_is_a_failed_read_after_a_minute_and_the_next_one_runs()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");
        var hung = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = snapshot.Refresh(() => hung.Task);

        Volatile.Write(ref now, 60_000 - 1);
        await snapshot.Refresh(() => Task.FromResult("too-soon"));
        snapshot.Busy.Should().BeTrue();
        snapshot.Current.Should().Be("initial");

        Volatile.Write(ref now, 60_000);
        await snapshot.Refresh(() => Task.FromResult("inside-the-interval"));
        snapshot.Busy.Should().BeFalse();
        snapshot.Current.Should().Be("initial");
        snapshot.Reads.Should().Be(0);
        snapshot.Wait.Should().Be(1_000);

        Volatile.Write(ref now, 60_000 + 1_000);
        await snapshot.Refresh(() => Task.FromResult("next"));
        snapshot.Current.Should().Be("next");
    }

    [TestMethod]
    public async Task A_read_that_never_completes_counts_toward_the_longer_wait()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");
        await snapshot.Refresh(() => Task.FromException<string>(new InvalidOperationException("boom")));
        snapshot.Wait.Should().Be(1_000);
        Volatile.Write(ref now, 1_000);
        var hung = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = snapshot.Refresh(() => hung.Task);

        Volatile.Write(ref now, 1_000 + 60_000);
        await snapshot.Refresh(() => Task.FromResult("inside-the-interval"));

        snapshot.Wait.Should().Be(2_000);
    }

    [TestMethod]
    public async Task A_read_given_up_on_is_thrown_away_when_it_lands()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");
        var hung = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var landed = snapshot.Refresh(() => hung.Task);
        Volatile.Write(ref now, 60_000);
        await snapshot.Refresh(() => Task.FromResult("inside-the-interval"));

        Volatile.Write(ref now, 60_000 + 400);
        hung.SetResult("late");
        await landed;

        snapshot.Current.Should().Be("initial");
        snapshot.Reads.Should().Be(0);
        snapshot.Wait.Should().Be(600);
    }

    [TestMethod]
    public async Task A_read_given_up_on_does_not_free_the_read_that_replaced_it()
    {
        long now = 0;
        var snapshot = new PolledSnapshot<string>(0, () => Volatile.Read(ref now), "initial");
        var hung = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var landed = snapshot.Refresh(() => hung.Task);
        Volatile.Write(ref now, 60_000);
        var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replaced = snapshot.Refresh(() => slow.Task);
        snapshot.Busy.Should().BeTrue();

        hung.SetResult("late");
        await landed;
        snapshot.Busy.Should().BeTrue();
        snapshot.Current.Should().Be("initial");
        var starts = 0;
        await snapshot.Refresh(() =>
        {
            starts++;
            return Task.FromResult("second-in-flight");
        });
        starts.Should().Be(0);

        slow.SetResult("next");
        await replaced;
        snapshot.Busy.Should().BeFalse();
        snapshot.Current.Should().Be("next");
        snapshot.Reads.Should().Be(1);
    }

    [TestMethod]
    public async Task A_read_given_up_on_is_logged_once()
    {
        var log = new Recorder();
        Log.Use(log);
        long now = 0;
        var snapshot = new PolledSnapshot<string>(1_000, () => Volatile.Read(ref now), "initial");
        var hung = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = snapshot.Refresh(() => hung.Task);

        Volatile.Write(ref now, 60_000);
        await snapshot.Refresh(() => Task.FromResult("inside-the-interval"));
        Volatile.Write(ref now, 60_000 + 500);
        await snapshot.Refresh(() => Task.FromResult("inside-the-interval"));

        log.Warnings.Should().Equal("a snapshot read did not finish in a minute and was given up on");
    }

    private sealed record Reading(int Level);

    private sealed class Recorder : ILog
    {
        public List<string> Warnings { get; } = [];

        public void Write(LogSeverity severity, string area, string message, Exception? error = null)
        {
            if (severity == LogSeverity.Warning)
            {
                Warnings.Add(message);
            }
        }
    }

    private static async Task Drain()
    {
        for (var i = 0; i < 32; i++)
        {
            await Task.Yield();
        }
    }

    private static async Task Until(Func<bool> done)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!done())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("The polled snapshot did not settle.");
            }

            await Task.Yield();
        }
    }
}
