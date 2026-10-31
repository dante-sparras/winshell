using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class IpcShowTests
{
    private const string Qml = """
        import Winshell
        import Winshell.Io
        ShellRoot {
          IpcHandler {
            target: "notes"
            signal saved(name, size)
            signal cleared()
            function count(): int { return 0 }
            function take(items: var): void {}
          }
        }
        """;

    [Fact]
    public void Show_lists_the_signals_wait_and_listen_can_use()
    {
        using var files = new TestShell(("shell.qml", Qml));
        using var host = Start(files);

        var shown = host.Ipc.Show();

        Assert.Contains("  signal saved(name, size)\n", shown);
        Assert.Contains("  signal cleared()\n", shown);
    }

    [Fact]
    public void Describe_keeps_a_signal_parameter_type()
    {
        using var files = new TestShell(("shell.qml", """
            import Winshell
            import Winshell.Io
            ShellRoot {
              IpcHandler {
                target: "notes"
                property string title: "notes"
                property var extra: 1
                signal opened(query: string)
                signal saved(name, size)
                function hello(name: string): string { return name }
                function take(items: var): void {}
              }
            }
            """));
        using var host = Start(files);

        var target = Assert.Single(host.Ipc.Describe());
        var hello = Assert.Single(target.Functions);
        Assert.Equal("hello", hello.Name);
        Assert.Equal("string", hello.Returns);
        Assert.Equal("string", Assert.Single(hello.Parameters).Type);
        Assert.Equal("string", Assert.Single(target.Properties).Type);
        Assert.Equal("string", Assert.Single(target.Signals.Single(s => s.Name == "opened").Parameters).Type);
        Assert.All(target.Signals.Single(s => s.Name == "saved").Parameters, p => Assert.Null(p.Type));
        Assert.Contains("  signal saved(name, size)\n", host.Ipc.Show(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_function_ipc_cannot_call_says_why()
    {
        using var files = new TestShell(("shell.qml", Qml));
        using var host = Start(files);

        var error = Assert.Throws<IpcException>(() => host.Ipc.Call("notes", "take", ["x"]));

        Assert.Contains("a type IPC can't pass", error.Message);
        Assert.Throws<IpcException>(() => host.Ipc.Call("notes", "missing", []));
    }

    private static ShellHost Start(TestShell files)
    {
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, new FakePlatform(), files.Log, null);
        Assert.True(host.Start(), string.Join(Environment.NewLine, files.Messages));
        files.Loop.Process();
        return host;
    }
}
