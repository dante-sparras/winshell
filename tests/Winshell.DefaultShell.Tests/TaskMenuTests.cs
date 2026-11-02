using Winshell.Apps;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

/// <summary>The rows TaskMenu.qml builds, as literals: jump list, the app, one pin slot, then close.</summary>
public sealed class TaskMenuTests
{
    private sealed record Entry(string Id, string Name, string Icon, string AppId) : ITaskApp;

    private sealed record Win(string Title, string Executable) : ITaskWindow
    {
        public string AppId => "";

        public string Icon => "";

        public bool Activated => false;

        public bool Minimized => false;
    }

    private static readonly Entry Notes = new("notes", "Notes", "notes-icon", "Contoso.Notes");

    private static TaskItem App(bool pinned, int windows) =>
        new("app:notes", pinned ? "notes" : "", Notes, [.. Enumerable.Range(0, windows).Select(i => new Win("Title " + i, @"C:\notes.exe"))], "notes-icon", "notes");

    private static AppJumpList Jump(bool available, params string[] names)
    {
        var list = new AppJumpList();
        list.Apply(available, names);
        return list;
    }

    [Fact]
    public void An_unpinned_app_with_no_windows_and_no_jump_list_has_three_rows() =>
        Assert.Equal(
            [
                new TaskMenuRow("unavailable", "Jump list can't be read", "", "", false),
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Pin to taskbar", "\uE718", "", true),
            ],
            TaskMenu.Rows(App(false, 0), Jump(false)));

    [Fact]
    public void A_pinned_app_with_no_windows_unpins_and_has_nothing_to_close() =>
        Assert.Equal(
            [
                new TaskMenuRow("unavailable", "Jump list can't be read", "", "", false),
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Unpin from taskbar", "\uE77A", "", true),
            ],
            TaskMenu.Rows(App(true, 0), Jump(false)));

    [Fact]
    public void One_window_closes_with_the_singular_row() =>
        Assert.Equal(
            [
                new TaskMenuRow("unavailable", "Jump list can't be read", "", "", false),
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Pin to taskbar", "\uE718", "", true),
                new TaskMenuRow("close", "Close window", "\uE8BB", "", true),
            ],
            TaskMenu.Rows(App(false, 1), Jump(false)));

    [Fact]
    public void Several_windows_close_together_and_a_pinned_app_unpins() =>
        Assert.Equal(
            [
                new TaskMenuRow("unavailable", "Jump list can't be read", "", "", false),
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Unpin from taskbar", "\uE77A", "", true),
                new TaskMenuRow("close", "Close all windows", "\uE8BB", "", true),
            ],
            TaskMenu.Rows(App(true, 2), Jump(false)));

    [Fact]
    public void A_jump_list_that_was_read_and_is_empty_has_no_destination_and_no_warning() =>
        Assert.Equal(
            [
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Pin to taskbar", "\uE718", "", true),
            ],
            TaskMenu.Rows(App(false, 0), Jump(true)));

    [Fact]
    public void An_empty_jump_list_on_a_pinned_app_with_one_window_still_closes_that_window() =>
        Assert.Equal(
            [
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Unpin from taskbar", "\uE77A", "", true),
                new TaskMenuRow("close", "Close window", "\uE8BB", "", true),
            ],
            TaskMenu.Rows(App(true, 1), Jump(true)));

    [Fact]
    public void Destinations_come_first_one_row_each_and_cannot_be_chosen() =>
        Assert.Equal(
            [
                new TaskMenuRow("dest:0", "Budget.xlsx", "", "", false),
                new TaskMenuRow("dest:1", "Notes.txt", "", "", false),
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Pin to taskbar", "\uE718", "", true),
                new TaskMenuRow("close", "Close all windows", "\uE8BB", "", true),
            ],
            TaskMenu.Rows(App(false, 2), Jump(true, "Budget.xlsx", "Notes.txt")));

    [Fact]
    public void Destinations_on_a_pinned_app_with_no_windows_stop_before_close() =>
        Assert.Equal(
            [
                new TaskMenuRow("dest:0", "Budget.xlsx", "", "", false),
                new TaskMenuRow("launch", "Notes", "", "notes-icon", true),
                new TaskMenuRow("pin", "Unpin from taskbar", "\uE77A", "", true),
            ],
            TaskMenu.Rows(App(true, 0), Jump(true, "Budget.xlsx")));

    [Fact]
    public void One_window_with_no_app_is_named_by_its_title_and_keeps_its_icon()
    {
        var task = new TaskItem("window:budget", "", null, [new Win("Budget", @"C:\budget.exe")], "budget-icon", @"C:\budget.exe");

        Assert.Equal(
            [
                new TaskMenuRow("launch", "Budget", "", "budget-icon", true),
                new TaskMenuRow("pin", "Pin to taskbar", "\uE718", "", true),
                new TaskMenuRow("close", "Close window", "\uE8BB", "", true),
            ],
            TaskMenu.Rows(task, Jump(true)));
    }

    [Fact]
    public void Several_windows_with_no_app_are_named_by_the_program()
    {
        var task = new TaskItem(
            "window:tool",
            "",
            null,
            [new Win("Untitled", @"C:\Tools\tool.exe"), new Win("Notes", @"C:\Tools\tool.exe")],
            "tool-icon",
            @"C:\Tools\tool.exe");

        Assert.Equal("tool", TaskMenu.Rows(task, Jump(true)).Single(row => row.Id == "launch").Label);
        Assert.Equal("Close all windows", TaskMenu.Rows(task, Jump(false)).Single(row => row.Id == "close").Label);
    }

    [Fact]
    public void The_empty_bar_menu_is_taskbar_settings()
    {
        Assert.Equal("Taskbar", TaskMenu.BarName);
        Assert.Equal(new TaskMenuRow("taskbarSettings", "Taskbar settings", "\uE713", "", true), TaskMenu.Settings);
    }
}
