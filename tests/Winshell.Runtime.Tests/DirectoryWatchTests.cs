using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public sealed class DirectoryWatchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public DirectoryWatchTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void A_file_made_right_after_start_is_reported()
    {
        using var seen = new ManualResetEventSlim();
        using var watch = DirectoryWatch.Start(_directory, DirectoryWatch.FileChanges, name =>
        {
            if (name == "made.qml")
                seen.Set();
        }, () => { });
        Assert.NotNull(watch);

        File.WriteAllText(Path.Combine(_directory, "made.qml"), "");

        Assert.True(seen.Wait(TimeSpan.FromSeconds(10)), "The new file was not reported.");
    }

    [Fact]
    public void A_watch_outlives_the_thread_that_started_it()
    {
        using var seen = new ManualResetEventSlim();
        DirectoryWatch? watch = null;
        var starter = new Thread(() => watch = DirectoryWatch.Start(_directory, DirectoryWatch.FileChanges, name =>
        {
            if (name == "made.qml")
                seen.Set();
        }, () => { }));
        starter.Start();
        starter.Join();
        using (watch)
        {
            Assert.NotNull(watch);

            File.WriteAllText(Path.Combine(_directory, "made.qml"), "");

            Assert.True(seen.Wait(TimeSpan.FromSeconds(10)), "The new file was not reported.");
        }
    }

    [Fact]
    public void Deleting_the_folder_ends_the_watch()
    {
        using var lost = new ManualResetEventSlim();
        using var watch = DirectoryWatch.Start(_directory, DirectoryWatch.FileChanges, _ => { }, lost.Set);
        Assert.NotNull(watch);

        Directory.Delete(_directory, true);

        Assert.True(lost.Wait(TimeSpan.FromSeconds(10)), "The watch did not report that it ended.");
    }

    [Fact]
    public void A_missing_folder_is_not_watched()
    {
        Assert.Null(DirectoryWatch.Start(Path.Combine(_directory, "missing"), DirectoryWatch.FileChanges, _ => { }, () => { }));
    }

    [Fact]
    public void Disposing_ends_the_watch_without_reporting_it()
    {
        var lost = false;
        var watch = DirectoryWatch.Start(_directory, DirectoryWatch.FileChanges, _ => { }, () => lost = true);
        Assert.NotNull(watch);

        watch.Dispose();

        Assert.False(lost);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
