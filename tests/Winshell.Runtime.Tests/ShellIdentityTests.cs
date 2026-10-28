using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class ShellIdentityTests
{
    [Fact]
    public void Instance_ids_use_reversed_base_36()
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
            import Winshell
            ShellRoot {
              property string sid: Shell.shellId
              property string aid: Shell.appId
              property string iid: Shell.instanceId
              property bool watching: Shell.watchFiles
              property bool fromSettings: settings.watchFiles
              property ShellSettings shared: settings
            }
            """));
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        try
        {
            Assert.True(host.Start(), string.Join('\n', files.Messages));
            files.Loop.Process();
            var root = host.Root!;
            var shell = host.Engine!.FindNativeSingleton<ShellGlobal>()!;

            Assert.Equal("demo", root.ReadProperty("sid"));
            Assert.Equal("org.example.shell", root.ReadProperty("aid"));
            Assert.Equal(host.ShellId, shell.ShellId);
            Assert.Equal(host.AppId, shell.AppId);
            Assert.Equal(host.LaunchTime, shell.LaunchTime);
            Assert.Equal(
                ShellHost.Base36(Environment.ProcessId) + ShellHost.Base36(new DateTimeOffset(host.LaunchTime).ToUnixTimeSeconds()),
                host.InstanceId);
            Assert.Equal(host.InstanceId, root.ReadProperty("iid"));
            Assert.Same(host.Settings, root.ReadProperty("settings"));
            Assert.Same(host.Settings, root.ReadProperty("shared"));
            Assert.True((bool)root.ReadProperty("watching")!);
            Assert.True((bool)root.ReadProperty("fromSettings")!);

            var closed = 0;
            shell.Connect("lastWindowClosed", _ => closed++);
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
            import Winshell
            Reloadable { }
            """));
        var error = Assert.Throws<QmlCompileException>(() => shell.Load());
        Assert.Contains("base class", error.Message, StringComparison.Ordinal);
    }
}
