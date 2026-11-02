using System.ComponentModel;
using Winshell.Windows;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>An open window the taskbar can read and act on. A test supplies its own.</summary>
public interface IOpenWindow : ITaskWindow, INotifyPropertyChanged
{
    /// <summary>The window flashed to ask for attention and has not been activated since.</summary>
    bool Attention { get; }

    /// <summary>The screens the window is on.</summary>
    IReadOnlyList<ScreenInfo> Screens { get; }

    void Activate();

    void Minimize();

    /// <summary>Asks the window to close.</summary>
    void Close();
}

/// <summary>
/// An open window as the taskbar reads it. Equality is the window itself, so a fresh adapter for the same window
/// compares equal and <see cref="Tasks.Click"/> can find the one used last.
/// </summary>
public sealed record TaskWindow(AppWindow Window) : IOpenWindow
{
    public string AppId => Window.AppId;

    public string Executable => Window.Executable;

    public string Title => Window.Title;

    public string Icon => Window.Icon;

    public bool Activated => Window.Activated;

    public bool Minimized => Window.Minimized;

    public bool Attention => Window.Attention;

    public IReadOnlyList<ScreenInfo> Screens => Window.Screens;

    public void Activate() => Window.Activate();

    public void Minimize() => Window.Minimized = true;

    public void Close() => Window.Close();

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => Window.PropertyChanged += value;
        remove => Window.PropertyChanged -= value;
    }
}
