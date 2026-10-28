using Winshell.Runtime.Quickshell;

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

    private static ShellHost Host(TestShell shell) => new(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log, () =>
    {
        var modules = StandardModules.Create();
        modules.Module("Broken").Type<Thrower>("Thrower");
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
}
