using Winshell.Runtime.Quickshell;

namespace Winshell.Runtime.Tests;

public class ShellIdentityTests
{
    [Fact]
    public void Instance_ids_use_quickshells_reversed_base_36()
    {
        Assert.Equal("0", ShellHost.Base36(0));
        Assert.Equal("1", ShellHost.Base36(1));
        Assert.Equal("01", ShellHost.Base36(36));
    }

    [Fact]
    public void The_shell_and_its_settings_share_one_identity()
    {
        using var files = new TestShell(("shell.qml", """
            //@ pragma ShellId demo
            //@ pragma AppId org.example.shell
            import Quickshell
            ShellRoot {
              property string sid: Quickshell.shellId
              property string aid: Quickshell.appId
              property string iid: Quickshell.instanceId
              property bool watching: Quickshell.watchFiles
              property bool fromSettings: settings.watchFiles
              property QuickshellSettings shared: settings
            }
            """));
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        try
        {
            Assert.True(host.Start(), string.Join('\n', files.Messages));
            files.Loop.Process();
            var root = host.Root!;
            var quickshell = host.Engine!.FindNativeSingleton<QuickshellGlobal>()!;

            Assert.Equal("demo", root.ReadProperty("sid"));
            Assert.Equal("org.example.shell", root.ReadProperty("aid"));
            Assert.Equal(host.ShellId, quickshell.ShellId);
            Assert.Equal(host.AppId, quickshell.AppId);
            Assert.Equal(host.LaunchTime, quickshell.LaunchTime);
            Assert.Equal(
                ShellHost.Base36(Environment.ProcessId) + ShellHost.Base36(new DateTimeOffset(host.LaunchTime).ToUnixTimeSeconds()),
                host.InstanceId);
            Assert.Equal(host.InstanceId, root.ReadProperty("iid"));
            Assert.Same(host.Settings, root.ReadProperty("settings"));
            Assert.Same(host.Settings, root.ReadProperty("shared"));
            Assert.True((bool)root.ReadProperty("watching")!);
            Assert.True((bool)root.ReadProperty("fromSettings")!);

            var closed = 0;
            quickshell.Connect("lastWindowClosed", _ => closed++);
            host.Settings.Emit("lastWindowClosed");

            Assert.Equal(1, closed);
            host.Settings.WatchFiles = false;
            Assert.False(host.WatchFiles);
            Assert.False((bool)root.ReadProperty("watching")!);
            Assert.False((bool)root.ReadProperty("fromSettings")!);
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void Reloadable_and_settings_cannot_be_created()
    {
        using var shell = new TestShell(("shell.qml", """
            import Quickshell
            Reloadable { }
            """));
        var error = Assert.Throws<QmlCompileException>(() => shell.Load());
        Assert.Contains("base class", error.Message, StringComparison.Ordinal);
    }
}
