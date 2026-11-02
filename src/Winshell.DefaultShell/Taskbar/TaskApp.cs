using System.ComponentModel;
using Winshell.Apps;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>What the taskbar can ask an app to do. A test supplies its own.</summary>
public interface ITaskLaunch
{
    void Launch();
}

/// <summary>An app in the app index as the taskbar reads it.</summary>
public sealed record TaskApp(AppEntry Entry) : ITaskApp, ITaskLaunch, INotifyPropertyChanged
{
    public string Id => Entry.Id;

    public string Name => Entry.Name;

    public string Icon => Entry.Icon;

    public string AppId => Entry.AppId;

    public void Launch() => Entry.Launch();

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => Entry.PropertyChanged += value;
        remove => Entry.PropertyChanged -= value;
    }
}
