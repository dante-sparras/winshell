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
        // The Windows key opens Start until Winshell Settings changes it.
        Assert.Contains("sequence: Preferences.startHotkey", shell);
        Assert.Contains("property string startHotkey: \"Win\"", Read(assembly, "DefaultShell/Preferences.qml"));
        Assert.Contains("toggleStart", shell);

        // The taskbar's Start hook opens it, and the taskbar shows Start pressed while it is open.
        Assert.Contains("onStartRequested: launcher.toggle()", shell);
        Assert.Contains("startOpen: launcher.visible", shell);
        var taskbar = Read(assembly, "DefaultShell/Taskbar.qml");
        Assert.Contains("label: \"Start\"", taskbar);
        Assert.Contains("bottom: true", taskbar);

        var launcher = Read(assembly, "DefaultShell/Launcher.qml");
        Assert.Contains("showIcons: launcher.visible", launcher);
    }

    [Fact]
    public void The_taskbar_search_button_opens_start_search()
    {
        var assembly = typeof(global::Winshell.DefaultShell).Assembly;
        var shell = Read(assembly, "DefaultShell/shell.qml");
        var launcher = Read(assembly, "DefaultShell/Launcher.qml");
        Assert.Contains("onSearchRequested: launcher.openSearch()", shell);
        Assert.Contains("searchOpen: launcher.searchOpen", shell);
        Assert.DoesNotContain("search-ms:", shell);
        Assert.Contains("function openSearch()", launcher);
    }

    [Fact]
    public void The_default_shell_embeds_Winshell_Settings()
    {
        var assembly = typeof(global::Winshell.DefaultShell).Assembly;
        var names = assembly.GetManifestResourceNames();
        foreach (var file in new[] { "SettingsWindow.qml", "SettingsView.qml", "SettingsCard.qml", "Preferences.qml", "settings.js" })
            Assert.Contains("DefaultShell/" + file, names);
        var shell = Read(assembly, "DefaultShell/shell.qml");
        Assert.Contains("SettingsWindow {", shell);
        Assert.Contains("onSettingsRequested: settings.open(\"taskbar\")", shell);
    }

    [Fact]
    public void Settings_refreshes_startup_when_its_window_is_shown_or_comes_forward()
    {
        var window = Read(typeof(global::Winshell.DefaultShell).Assembly, "DefaultShell/SettingsWindow.qml");
        Assert.Contains("onVisibleChanged: if (visible) Startup.refresh()", window);
        Assert.Contains("onActiveChanged: if (active) Startup.refresh()", window);
        Assert.Contains("if (showing)", window);
        Assert.Contains("Startup.refresh()", window);
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
