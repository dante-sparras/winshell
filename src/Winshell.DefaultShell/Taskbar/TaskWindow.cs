using System.ComponentModel;
using Winshell.Windows;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>What a window tells the taskbar besides the fields <see cref="Tasks"/> reads: attention, and which screens it is on.</summary>
public interface ITaskWindowView
{
    bool Attention { get; }

    IReadOnlyList<ScreenInfo> Screens { get; }
}

/// <summary>What the taskbar can ask a window to do. A test supplies its own.</summary>
public interface ITaskControl
{
    void Activate();

    void Minimize();
}

/// <summary>
/// An open window as the taskbar reads it. Equality is the window itself, so a fresh adapter for the same window
/// compares equal and <see cref="Tasks.Click"/> can find the one used last.
/// </summary>
public sealed record TaskWindow(AppWindow Window) : ITaskWindow, ITaskWindowView, ITaskControl, INotifyPropertyChanged
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

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => Window.PropertyChanged += value;
        remove => Window.PropertyChanged -= value;
    }
}
