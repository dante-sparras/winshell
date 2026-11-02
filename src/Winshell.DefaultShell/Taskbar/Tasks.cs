using System.Text.RegularExpressions;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>What the taskbar reads of an app in the app index.</summary>
public interface ITaskApp
{
    string Id { get; }

    string Name { get; }

    string Icon { get; }

    string AppId { get; }
}

/// <summary>What the taskbar reads of an open window.</summary>
public interface ITaskWindow
{
    string AppId { get; }

    string Executable { get; }

    string Title { get; }

    string Icon { get; }

    bool Activated { get; }

    bool Minimized { get; }
}

/// <summary>A button on the taskbar: an app, with its windows.</summary>
public sealed record TaskItem(string Key, string Pin, ITaskApp? Entry, IReadOnlyList<ITaskWindow> Windows, string Icon, string PinId)
{
    public bool Pinned => Pin.Length > 0;
}

/// <summary>What a left click on a button does.</summary>
public abstract record TaskClick
{
    private TaskClick()
    {
    }

    /// <summary>The app is not running.</summary>
    public sealed record Launch : TaskClick;

    /// <summary>The only window is in front.</summary>
    public sealed record Minimize(ITaskWindow Window) : TaskClick;

    public sealed record Activate(ITaskWindow Window) : TaskClick;
}

/// <summary>
/// How the taskbar turns pinned apps and open windows into buttons, the way Windows 11 does: pinned apps first in the
/// order they were pinned, then every other running app in the order it opened, each app's windows grouped on one
/// button.
/// </summary>
public static partial class Tasks
{
    /// <summary>The name of a program file without its folder or extension, in lower case: the app id Winshell gives the windows of a program that has no AppUserModelID.</summary>
    public static string ProgramId(string path)
    {
        var name = path.Split('\\', '/')[^1];
        var dot = name.LastIndexOf('.');
        return (dot > 0 ? name[..dot] : name).ToLowerInvariant();
    }

    /// <summary>A pin is an app's id in the app index, or the path of a program the index does not have.</summary>
    public static bool IsProgramPath(string pin) => DrivePath().IsMatch(pin) || pin.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>What a button is keyed by: the app index entry when there is one, else the app id its windows carry.</summary>
    public static string PinKey(string pin, Func<string, ITaskApp?> find) =>
        find(pin) is { } entry
            ? "app:" + entry.Id.ToLowerInvariant()
            : "window:" + (IsProgramPath(pin) ? ProgramId(pin) : pin.ToLowerInvariant());

    public static string WindowKey(ITaskWindow window, Func<string, ITaskApp?> find)
    {
        var entry = window.AppId.Length > 0 ? find(window.AppId) : null;
        if (entry is not null)
            return "app:" + entry.Id.ToLowerInvariant();
        return "window:" + (window.AppId.Length > 0 ? window.AppId : ProgramId(window.Executable)).ToLowerInvariant();
    }

    /// <summary>The AppUserModelID a jump list is read with, or <c>""</c> when the task has none.</summary>
    public static string AppUserModelId(TaskItem? task)
    {
        if (task?.Entry is { AppId.Length: > 0 } entry)
            return entry.AppId;
        return task?.Windows is [{ AppId.Length: > 0 } first, ..] ? first.AppId : "";
    }

    /// <summary>
    /// The taskbar's buttons. <paramref name="windows"/> is the window list in the order the windows opened,
    /// <paramref name="find"/> looks an app up in the index, and <paramref name="shows"/> says whether this taskbar
    /// shows a window. Pinned apps are left out when <paramref name="showPins"/> is false, unless one of their windows
    /// is shown.
    /// </summary>
    public static IReadOnlyList<TaskItem> Build(IEnumerable<ITaskWindow> windows, IEnumerable<string> pins, Func<string, ITaskApp?> find, Func<ITaskWindow, bool> shows, bool showPins)
    {
        var byKey = new Dictionary<string, Draft>();
        var order = new List<Draft>();
        Draft Task(string key)
        {
            if (!byKey.TryGetValue(key, out var draft))
            {
                byKey[key] = draft = new Draft(key);
                order.Add(draft);
            }
            return draft;
        }
        foreach (var pin in pins)
        {
            var key = PinKey(pin, find);
            if (byKey.ContainsKey(key))
                continue;
            var draft = Task(key);
            draft.Pin = pin;
            draft.Entry = find(pin);
        }
        foreach (var window in windows)
        {
            if (!shows(window))
                continue;
            var draft = Task(WindowKey(window, find));
            if (draft.Entry is null && window.AppId.Length > 0)
                draft.Entry = find(window.AppId);
            draft.Windows.Add(window);
        }
        // A pin the app index does not have yet, or no longer has, waits without a button: there is nothing to launch.
        return [.. order
            .Where(t => t.Windows.Count > 0 || (showPins && (t.Entry is not null || IsProgramPath(t.Pin))))
            .Select(t => new TaskItem(
                t.Key,
                t.Pin,
                t.Entry,
                t.Windows,
                t.Entry?.Icon ?? (t.Windows.Count > 0 ? t.Windows[0].Icon : t.Pin),
                // The id to pin this app by: its entry in the app index, or the program it runs.
                t.Pin.Length > 0 ? t.Pin : t.Entry?.Id ?? t.Windows[0].Executable))];
    }

    /// <summary>The app's name: its name in the app index, or the title of its one window, or its program's name.</summary>
    public static string Name(TaskItem task)
    {
        if (task.Entry is { } entry)
            return entry.Name;
        return task.Windows switch
        {
            [var only] => only.Title,
            [var first, ..] => ProgramId(first.Executable.Length > 0 ? first.Executable : first.AppId),
            _ => ProgramId(task.Pin),
        };
    }

    /// <summary>What Narrator reads for a button, as it reads Explorer's.</summary>
    public static string AccessibleName(TaskItem task) => task.Windows.Count switch
    {
        0 => $"{Name(task)} pinned",
        1 => $"{Name(task)} - 1 running window",
        var count => $"{Name(task)} - {count} running windows",
    };

    /// <summary>
    /// What a left click does: launch an app that is not running, minimize the only window when it is in front,
    /// otherwise bring a window forward. With several windows, a click when one is in front goes to the next one, and
    /// otherwise to the one used last.
    /// </summary>
    public static TaskClick Click(TaskItem task, ITaskWindow? lastUsed)
    {
        var windows = task.Windows;
        if (windows.Count == 0)
            return new TaskClick.Launch();
        var front = windows.ToList().FindIndex(w => w.Activated && !w.Minimized);
        if (windows.Count == 1)
            return front == 0 ? new TaskClick.Minimize(windows[0]) : new TaskClick.Activate(windows[0]);
        if (front >= 0)
            return new TaskClick.Activate(windows[(front + 1) % windows.Count]);
        return new TaskClick.Activate(lastUsed is not null && windows.Contains(lastUsed) ? lastUsed : windows[0]);
    }

    [GeneratedRegex(@"^[a-z]:[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex DrivePath();

    private sealed class Draft(string key)
    {
        public string Key { get; } = key;

        public string Pin { get; set; } = "";

        public ITaskApp? Entry { get; set; }

        public List<ITaskWindow> Windows { get; } = [];
    }
}
