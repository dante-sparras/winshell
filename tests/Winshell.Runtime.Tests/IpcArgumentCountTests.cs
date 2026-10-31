using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class IpcArgumentCountTests
{
    private const string Qml = """
        import Winshell
        ShellRoot {
          IpcHandler {
            target: "notes"
            function one(text: string): void {}
            function two(a: string, b: string): void {}
          }
        }
        """;

    [Theory]
    [InlineData("one", "takes 1 argument, 0 given")]
    [InlineData("two", "takes 2 arguments, 0 given")]
    public void The_error_counts_arguments_in_plain_english(string function, string message)
    {
        using var files = new TestShell(("shell.qml", Qml));
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, new FakePlatform(), files.Log, null);
        Assert.True(host.Start(), string.Join("
", files.Messages));
        files.Loop.Process();

        var error = Assert.Throws<IpcException>(() => host.Ipc.Call("notes", function, []));

        Assert.EndsWith(message, error.Message);
    }
}
