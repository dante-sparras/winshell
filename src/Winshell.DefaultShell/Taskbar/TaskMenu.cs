using Winshell.Apps;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>One row of a taskbar menu. A destination, and the row that says the jump list cannot be read, are not actions.</summary>
public readonly record struct TaskMenuRow(string Id, string Label, string Glyph, string Icon, bool Enabled);

/// <summary>
/// The rows of the menu a task button opens, in the order Windows 11 shows them: the jump list, the app's name,
/// pin or unpin, then close when it has windows.
/// </summary>
public static class TaskMenu
{
    private const string PinGlyph = "\uE718";
    private const string UnpinGlyph = "\uE77A";
    private const string CloseGlyph = "\uE8BB";

    /// <summary>What Narrator calls the menu a right click on an empty part of the bar opens.</summary>
    public const string BarName = "Taskbar";

    /// <summary>The only row of that menu.</summary>
    public static TaskMenuRow Settings { get; } = new("taskbarSettings", "Taskbar settings", "\uE713", "", true);

    /// <summary>The rows for <paramref name="task"/> from the jump list as it stands now.</summary>
    public static IReadOnlyList<TaskMenuRow> Rows(TaskItem task, AppJumpList jump)
    {
        var rows = new List<TaskMenuRow>();
        if (!jump.Available)
            rows.Add(new TaskMenuRow("unavailable", "Jump list can't be read", "", "", false));
        else
        {
            var names = jump.Destinations;
            for (var i = 0; i < names.Count; i++)
                rows.Add(new TaskMenuRow("dest:" + i, names[i], "", "", false));
        }
        rows.Add(new TaskMenuRow("launch", Tasks.Name(task), "", task.Icon, true));
        // One slot, so the row stays put while the app is pinned or unpinned.
        rows.Add(task.Pinned
            ? new TaskMenuRow("pin", "Unpin from taskbar", UnpinGlyph, "", true)
            : new TaskMenuRow("pin", "Pin to taskbar", PinGlyph, "", true));
        if (task.Windows.Count > 0)
            rows.Add(new TaskMenuRow("close", task.Windows.Count == 1 ? "Close window" : "Close all windows", CloseGlyph, "", true));
        return rows;
    }
}
