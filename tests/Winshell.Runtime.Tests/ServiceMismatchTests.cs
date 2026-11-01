using System.IO.Pipes;
using Winshell.Runtime;
using Winshell.Runtime.Shell;
using Xunit;

namespace Winshell.Runtime.Tests;

/// <summary>Services do what their reference page says.</summary>
public class ServiceMismatchTests
{
    [Fact]
    public async Task A_client_that_disconnects_is_released()
    {
        var name = "winshell-clients-" + Guid.NewGuid().ToString("N");
        using var shell = new TestShell(("shell.qml", $$"""
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              id: root
              property int accepted: 0
              property int gone: 0
              property int disconnected: 0
              SocketServer {
                path: "\\\\.\\pipe\\{{name}}"
                active: true
                handler: Socket {
                  Component.onCompleted: root.accepted = root.accepted + 1
                  Component.onDestruction: root.gone = root.gone + 1
                  onConnectedChanged: if (!connected) root.disconnected = root.disconnected + 1
                }
              }
            }
            """));
        var root = shell.Load();

        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(3000, TestContext.Current.CancellationToken);
            await Pump(shell, () => Number(root, "accepted") == 1);
            Assert.Equal(1, Number(root, "accepted"));
            Assert.Equal(0, Number(root, "gone"));
        }

        await Pump(shell, () => Number(root, "gone") == 1);
        Assert.Equal(1, Number(root, "disconnected"));
        Assert.Equal(1, Number(root, "gone"));
    }

    [Fact]
    public void A_change_inside_a_nested_json_object_reaches_the_adapter()
    {
        using var shell = new TestShell(("shell.qml", """
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              property int updates: 0
              JsonAdapter {
                id: adapter
                property JsonObject outer: JsonObject {
                  property JsonObject inner: JsonObject {
                    property int value: 1
                  }
                }
                onAdapterUpdated: updates = updates + 1
              }
              function change() { adapter.outer.inner.value = 2 }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "change()");

        Assert.Equal(1.0, shell.Eval(root, "updates"));
    }

    private static int Number(QObject root, string name) => Convert.ToInt32(root.ReadProperty(name), System.Globalization.CultureInfo.InvariantCulture);

    private static Task Pump(TestShell shell, Func<bool> done) => Wait.UntilAsync(done, "the client", shell.Loop.Process);
}
