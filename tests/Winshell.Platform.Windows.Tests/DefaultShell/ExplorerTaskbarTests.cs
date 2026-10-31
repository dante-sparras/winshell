using Winshell.Platform.Windows.Services.ExplorerTaskbar;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// Explorer's taskbar is hidden only when asked, and only through a stand-in. Nothing here constructs the Win32 taskbar.
/// </summary>
[Collection(ThemeCollection.Name)]
public class ExplorerTaskbarTests
{
    private sealed class Fake : IExplorerTaskbar
    {
        public int Created { get; set; }
        public int Hides { get; private set; }
        public int Restores { get; private set; }
        public void Hide() => Hides++;
        public void Restore() => Restores++;
    }

    private static ExplorerTaskbarSession Session(Fake fake) => new(() =>
    {
        fake.Created++;
        return fake;
    });

    [Fact]
    public void Leaving_it_off_never_calls_the_taskbar_and_exit_does_not_either()
    {
        var fake = new Fake();
        var session = Session(fake);

        session.Restore();
        session.Dispose();
        session.Dispose();

        Assert.Equal(0, fake.Created);
        Assert.Equal(0, fake.Hides);
        Assert.Equal(0, fake.Restores);
    }

    [Fact]
    public void Hiding_and_restoring_each_happen_once_and_exit_puts_it_back()
    {
        var fake = new Fake();
        var session = Session(fake);

        session.Hide();
        session.Hide();
        Assert.Equal(1, fake.Created);
        Assert.Equal(1, fake.Hides);
        Assert.Equal(0, fake.Restores);

        session.Restore();
        session.Restore();
        Assert.Equal(1, fake.Restores);

        session.Hide();
        session.Dispose();
        session.Dispose();
        Assert.Equal(2, fake.Hides);
        Assert.Equal(2, fake.Restores);
        Assert.Equal(1, fake.Created);
    }

    [Fact]
    public void The_preference_starts_off_and_hides_Explorer_only_when_set()
    {
        var fake = new Fake();
        var session = Session(fake);
        var shell = new DefaultShellHarness("""
            import Winshell
            import QtQuick
            Item {
              property bool hide: false
              property bool preference: Preferences.hideExplorerTaskbar
              onHideChanged: Preferences.hideExplorerTaskbar = hide
            }
            """, configure: engine => engine.AddService(session));
        var closed = false;
        try
        {
            Assert.False((bool)shell.Root.ReadProperty("preference")!);
            Assert.Equal(0, fake.Created);
            Assert.Equal(0, fake.Hides);
            Assert.Equal(0, fake.Restores);

            shell.Root.WriteProperty("hide", true);
            shell.Settle();
            Assert.True((bool)shell.Root.ReadProperty("preference")!);
            Assert.Equal(1, fake.Created);
            Assert.Equal(1, fake.Hides);

            shell.Root.WriteProperty("hide", false);
            shell.Settle();
            Assert.Equal(1, fake.Restores);

            shell.Root.WriteProperty("hide", true);
            shell.Settle();
            Assert.Equal(2, fake.Hides);
            Assert.Equal(1, fake.Restores);
            Assert.False(File.Exists(Path.Combine(shell.Directory, "preferences.toml")));

            // The shell closing does not put the taskbar back. The process exit does, once.
            shell.Dispose();
            closed = true;
            Assert.Equal(2, fake.Hides);
            Assert.Equal(1, fake.Restores);
            session.Dispose();
            Assert.Equal(2, fake.Restores);
        }
        finally
        {
            session.Dispose();
            if (!closed)
                shell.Dispose();
        }
    }
}
