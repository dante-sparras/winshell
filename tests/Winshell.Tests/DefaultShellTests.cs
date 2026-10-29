using System.Reflection;

namespace Winshell.Tests;

public class DefaultShellTests
{
    [Fact]
    public void The_default_shell_embeds_the_launcher()
    {
        var assembly = typeof(global::Winshell.DefaultShell).Assembly;
        var names = assembly.GetManifestResourceNames();
        Assert.Contains("DefaultShell/Launcher.qml", names);
        Assert.Contains("DefaultShell/LauncherView.qml", names);
        Assert.Contains("DefaultShell/launcher.js", names);

        var shell = Read(assembly, "DefaultShell/shell.qml");
        Assert.Contains("sequence: \"Win\"", shell);
        Assert.Contains("toggleStart", shell);

        var bar = Read(assembly, "DefaultShell/Bar.qml");
        Assert.Contains("toggleLauncher", bar);
        Assert.Contains("Accessible.name: \"Start\"", bar);
        Assert.Contains("anchors.bottom", bar);

        var launcher = Read(assembly, "DefaultShell/Launcher.qml");
        Assert.Contains("showIcons: launcher.visible", launcher);
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
