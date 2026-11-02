using Microsoft.UI.Xaml.Controls;
using Winshell;
using Winshell.Input;

namespace Winshell.ShellTests;

/// <summary>
/// The C# samples in <c>docs/</c> and the README. <see cref="DocsTests"/> requires each <c>csharp</c> fence to appear
/// in this file. Nothing here runs, and none of it opens a window.
/// </summary>
public static class GuideSamples
{
    private static IDisposable? _registration;

    /// <summary>Call this from <c>Main</c>, marked <c>[STAThread]</c>.</summary>
    public static void RunTheShell()
    {
        Shell.Run<AppShell>(new ShellOptions { ShellId = "MyShell" });
    }

    public static void APanel()
    {
        _ = new PanelWindow
        {
            Anchors = Edges.Left | Edges.Right | Edges.Bottom,
            Height = 48,
            ExclusiveZone = 48,
            Content = new TextBlock { Text = "Winshell" },
            Visible = true,
        };
    }

    public static void ANote(Shell shell)
    {
        // The registration stays for the life of the shell. The call below is a docs sample.
        _registration =
        shell.Ipc.Handle("notes", "hello", (string name) => "hi " + name);
        _ = _registration;
    }

    public static void AHotkey(Shell shell)
    {
        var hotkey = shell.NewHotkey();
        hotkey.Sequence = "Win+E";
        hotkey.Pressed += (_, _) => { };
    }

    public static void Reload(Shell shell)
    {
        if (!shell.Reload())
            shell.Log.Warn("shell", "Reload did not start.");
    }
}

/// <summary>The smallest shell: one panel. The <c>winshell</c> template does the same with its own view.</summary>
public sealed class AppShell : ShellConfiguration
{
    public override void Start(Shell shell)
    {
        _ = shell;
        GuideSamples.APanel();
    }
}

public sealed class ShellPreferences(string path, IEventLoop loop) : TomlFile(path, loop)
{
    [TomlComment("\"system\" follows Windows; or \"light\" or \"dark\".")]
    public string ColorScheme { get; set => Set(ref field, value); } = "system";
}
