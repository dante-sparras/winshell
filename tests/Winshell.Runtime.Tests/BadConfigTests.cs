using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// A configuration can make the runtime throw anything, not only a QML error. The shell reports it and keeps
/// running: with nothing on a first load, with the generation it had on a reload, and again once the files are fixed.
/// </summary>
public class BadConfigTests
{
    private const string Good = """
        import QtQml
        QtObject { objectName: "good" }
        """;

    private const string Bad = """
        import Broken
        Thrower { value: 1 }
        """;

    [Fact]
    public void A_first_load_that_throws_is_reported_and_the_fixed_config_loads()
    {
        using var shell = new TestShell(("shell.qml", Bad));
        using var host = Host(shell);

        Assert.False(host.Start());
        Assert.Null(host.Root);
        Assert.Contains(shell.Messages, m => m.StartsWith("Error scene: Failed to load configuration: ", StringComparison.Ordinal)
            && m.EndsWith("InvalidOperationException: the runtime failed", StringComparison.Ordinal));

        shell.Write("shell.qml", Good);
        host.Reload(hard: false);
        Assert.Equal("good", host.Root?.ObjectName);
    }

    [Fact]
    public void A_reload_that_throws_keeps_the_running_generation()
    {
        using var shell = new TestShell(("shell.qml", Good));
        using var host = Host(shell);
        Assert.True(host.Start());
        var running = host.Root!;

        shell.Write("shell.qml", Bad);
        host.Reload(hard: false);

        Assert.Same(running, host.Root);
        Assert.False(running.IsDestroyed);
        Assert.Contains(shell.Messages, m => m.StartsWith("Error scene: Failed to reload configuration: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reload_that_throws_while_handing_objects_over_keeps_the_running_generation()
    {
        const string Config = """
            import Winshell
            import Broken
            ShellRoot {
              objectName: "good"
              ThrowOnHandoff {}
            }
            """;
        using var shell = new TestShell(("shell.qml", Config));
        using var host = HandoffHost(shell);
        string? reported = "missing";
        host.Reloaded += error => reported = error;
        Assert.True(host.Start());
        var running = host.Root!;
        reported = "missing";

        host.Reload(hard: false);

        Assert.Same(running, host.Root);
        Assert.False(running.IsDestroyed);
        Assert.Contains("handoff failed", reported, StringComparison.Ordinal);
        Assert.Contains(shell.Messages, m => m.StartsWith("Error scene: Failed to reload configuration: ", StringComparison.Ordinal)
            && m.Contains("handoff failed", StringComparison.Ordinal));
        Assert.Contains(shell.Messages, m => m.Contains("System.InvalidOperationException: handoff failed", StringComparison.Ordinal)
            && m.Contains("at ", StringComparison.Ordinal));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("Configuration Loaded", StringComparison.Ordinal));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("Failed to hand the old configuration", StringComparison.Ordinal));
    }

    [Fact]
    public void A_first_load_that_throws_while_handing_objects_over_is_reported()
    {
        const string Config = """
            import Winshell
            import Broken
            ShellRoot {
              objectName: "good"
              ThrowOnHandoff { throwAlways: true }
            }
            """;
        using var shell = new TestShell(("shell.qml", Config));
        using var host = HandoffHost(shell);
        string? reported = "missing";
        host.Reloaded += error => reported = error;

        Assert.False(host.Start());

        Assert.Null(host.Root);
        Assert.Contains("handoff failed", reported, StringComparison.Ordinal);
        Assert.Contains(shell.Messages, m => m.StartsWith("Error scene: Failed to load configuration: ", StringComparison.Ordinal)
            && m.Contains("handoff failed", StringComparison.Ordinal));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("Configuration Loaded", StringComparison.Ordinal));
    }

    private static ShellHost Host(TestShell shell) => new(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log, () =>
    {
        var modules = StandardModules.Create();
        modules.Module("Broken").Type<Thrower>("Thrower");
        return modules;
    })
    { WatchFiles = false };

    private static ShellHost HandoffHost(TestShell shell) => new(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log, () =>
    {
        var modules = StandardModules.Create();
        modules.Module("Broken").Type<ThrowOnHandoff>("ThrowOnHandoff");
        return modules;
    })
    { WatchFiles = false };

    /// <summary>A type whose property fails the way a runtime bug would, with an exception QML knows nothing about.</summary>
    public sealed class Thrower : QObject
    {
        [Qml]
        public int Value
        {
            get => 0;
            set => throw new InvalidOperationException("the runtime failed");
        }
    }

    /// <summary>A reloadable whose handoff throws, after the new object already exists.</summary>
    public sealed class ThrowOnHandoff : Reloadable
    {
        private bool _throwAlways;

        [Qml]
        public bool ThrowAlways
        {
            get => _throwAlways;
            set => _throwAlways = value;
        }

        protected override void OnReload(QObject? oldInstance)
        {
            if (_throwAlways || oldInstance is not null)
                throw new InvalidOperationException("handoff failed");
        }
    }
}
