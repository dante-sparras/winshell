using System.ComponentModel;
using Winshell;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskPreviewTests
{
    [Fact]
    public void The_pointer_rests_before_a_preview_opens_and_leaving_cancels_it()
    {
        using var bar = new Bar();
        var notes = new Win { Id = 1, Title = "Notes" };
        bar.Set(Task("notes", "Notes", notes));
        var widthWhenOpened = -1d;
        var grabWhenOpened = true;
        bar.Preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(TaskPreview.IsOpen) || !bar.Preview.IsOpen)
                return;
            widthWhenOpened = bar.Preview.Width;
            grabWhenOpened = bar.Preview.GrabFocus;
        };

        bar.Preview.HoverButton("notes");
        bar.Advance(TaskPreview.OpenDelayMs - 1);
        Assert.False(bar.Preview.IsOpen);
        bar.Advance(1);
        Assert.True(bar.Preview.IsOpen);
        Assert.False(grabWhenOpened);
        Assert.True(widthWhenOpened > 0);
        Assert.Equal("notes", bar.Preview.TaskKey);
        Assert.Equal(notes, bar.Preview.Windows[0]);

        bar.Preview.Hide();
        bar.Preview.HoverButton("notes");
        bar.Advance(TaskPreview.OpenDelayMs - 1);
        bar.Preview.LeaveButton("notes");
        bar.Advance(TaskPreview.OpenDelayMs);
        Assert.False(bar.Preview.IsOpen);
    }

    [Fact]
    public void An_open_preview_moves_to_the_next_button_at_once()
    {
        using var bar = new Bar();
        bar.Set(Task("a", "A", new Win { Id = 1, Title = "A" }));
        bar.Set(Task("b", "B", new Win { Id = 2, Title = "B" }));
        bar.Open("a");

        bar.Preview.HoverButton("b");
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal("b", bar.Preview.TaskKey);
        Assert.Equal("B", bar.Preview.Windows[0].Title);
        Assert.False(bar.Preview.GrabFocus);
    }

    [Fact]
    public void Leaving_the_previous_button_does_not_cancel_the_button_the_pointer_is_on()
    {
        using var bar = new Bar();
        bar.Set(Task("a", "A", new Win { Id = 1, Title = "A" }));
        bar.Set(Task("b", "B", new Win { Id = 2, Title = "B" }));
        bar.Preview.HoverButton("a");
        bar.Preview.HoverButton("b");
        bar.Preview.LeaveButton("a");
        bar.Advance(TaskPreview.OpenDelayMs);
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal("b", bar.Preview.TaskKey);
        Assert.Equal("B", bar.Preview.Windows[0].Title);
    }

    [Fact]
    public void The_preview_closes_a_moment_after_the_pointer_leaves_both_the_button_and_it()
    {
        using var bar = new Bar();
        bar.Set(Task("notes", "Notes", new Win { Id = 1, Title = "Notes" }));
        bar.Open("notes");

        bar.Preview.LeaveButton("notes");
        bar.Advance(TaskPreview.CloseDelayMs - 1);
        Assert.True(bar.Preview.IsOpen);
        bar.Advance(1);
        Assert.False(bar.Preview.IsOpen);
        Assert.Equal("notes", bar.Preview.TaskKey);
        Assert.Equal(0, bar.Dismissed);
    }

    [Fact]
    public void The_pointer_inside_the_preview_or_back_on_its_button_keeps_it_open()
    {
        using var bar = new Bar();
        bar.Set(Task("notes", "Notes", new Win { Id = 1, Title = "Notes" }));
        bar.Open("notes");
        bar.Preview.LeaveButton("notes");
        bar.Preview.PointerInside(true);
        bar.Advance(TaskPreview.CloseDelayMs + TaskPreview.OpenDelayMs);
        Assert.True(bar.Preview.IsOpen);

        bar.Preview.PointerInside(false);
        bar.Advance(TaskPreview.CloseDelayMs - 1);
        bar.Preview.HoverButton("notes");
        bar.Advance(TaskPreview.CloseDelayMs);
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal("notes", bar.Preview.TaskKey);
    }

    [Fact]
    public void The_pointer_rests_on_a_window_before_Windows_peeks_at_it()
    {
        using var bar = new Bar();
        var window = new Win { Id = 1, Title = "Notes" };
        bar.Set(Task("notes", "Notes", window, new Win { Id = 2, Title = "Other" }));
        bar.Open("notes");

        bar.Preview.PointerOn(window);
        Assert.True(bar.Preview.ShowsClose(0));
        Assert.False(bar.Preview.ShowsClose(1));
        Assert.Null(bar.Preview.PeekWindow);
        bar.Advance(100);
        Assert.Null(bar.Preview.PeekWindow);
        bar.Advance(TaskPreview.PeekDelayMs - 100 - 1);
        Assert.Null(bar.Preview.PeekWindow);
        bar.Advance(1);
        Assert.Equal(window, bar.Preview.PeekWindow);
        Assert.Equal("Close Notes", TaskPreview.CloseName(window.Title));
    }

    [Fact]
    public void A_short_pass_does_not_peek_and_pointing_at_the_same_window_again_does_not_restart_the_wait()
    {
        using var passed = new Bar();
        var first = new Win { Id = 1, Title = "One" };
        var second = new Win { Id = 2, Title = "Two" };
        passed.Set(Task("notes", "Notes", first, second));
        passed.Open("notes");
        passed.Preview.PointerOn(first);
        passed.Advance(100);
        passed.Preview.PointerOn(second);
        passed.Advance(100);
        Assert.Null(passed.Preview.PeekWindow);
        passed.Preview.PointerOn(null);
        passed.Advance(TaskPreview.PeekDelayMs);
        Assert.Null(passed.Preview.PeekWindow);
        Assert.Null(passed.Preview.HoveredWindow);

        using var same = new Bar();
        var window = new Win { Id = 1, Title = "Notes" };
        same.Set(Task("notes", "Notes", window));
        same.Open("notes");
        same.Preview.PointerOn(window);
        same.Advance(200);
        same.Preview.PointerOn(new Win { Id = window.Id, Title = window.Title });
        same.Advance(TaskPreview.PeekDelayMs - 200 - 1);
        Assert.Null(same.Preview.PeekWindow);
        same.Advance(1);
        Assert.Same(window, same.Preview.PeekWindow);
    }

    [Fact]
    public void Leaving_a_window_ends_its_peek_and_closing_the_preview_does_not_bring_the_peek_back()
    {
        using var bar = new Bar();
        var window = new Win { Id = 1, Title = "Notes" };
        bar.Set(Task("notes", "Notes", window));
        bar.Open("notes");
        bar.Preview.PointerOn(window);
        bar.Advance(TaskPreview.PeekDelayMs);
        Assert.Equal(window, bar.Preview.PeekWindow);

        bar.Preview.PointerOn(null);
        Assert.Null(bar.Preview.PeekWindow);
        Assert.Null(bar.Preview.HoveredWindow);

        bar.Preview.PointerOn(window);
        bar.Advance(TaskPreview.PeekDelayMs);
        bar.Preview.Hide();
        Assert.Null(bar.Preview.PeekWindow);
        bar.Open("notes");
        Assert.Null(bar.Preview.PeekWindow);
        Assert.Null(bar.Preview.HoveredWindow);
        bar.Preview.PointerOn(window);
        bar.Advance(TaskPreview.PeekDelayMs - 1);
        Assert.Null(bar.Preview.PeekWindow);
        bar.Advance(1);
        Assert.Equal(window, bar.Preview.PeekWindow);
    }

    [Theory]
    [InlineData(1920, 3, false, 672, 176, 216)]
    [InlineData(1920, 9, false, 1884, 167, 204)]
    [InlineData(1280, 3, false, 672, 176, 216)]
    [InlineData(1280, 9, true, 336, 376, 133)]
    [InlineData(960, 9, true, 336, 376, 97)]
    [InlineData(270, 3, true, 238, 136, 71)]
    [InlineData(270, 9, true, 238, 376, 21)]
    public void Cards_shrink_to_fit_and_become_a_list_under_152_pixels(
        double workWidth, int count, bool listed, double width, double height, double cardWidth)
    {
        var layout = TaskPreview.Measure(count, workWidth, 1032);
        Assert.Equal(listed, layout.Listed);
        Assert.Equal(width, layout.Width);
        Assert.Equal(height, layout.Height);
        Assert.Equal(cardWidth, layout.CardWidth);
        Assert.True(layout.Width <= workWidth - 32);
        Assert.True(layout.Height <= 1032 * 0.6 + 16);
        if (listed)
            Assert.True(layout.ListWidth >= 96);
        else
        {
            // The title and the 24px close button sit above the live image, so a card has to hold them.
            Assert.True(layout.CardWidth >= 152);
            Assert.True(layout.CardHeight >= 36);
        }
    }

    [Fact]
    public void Twenty_windows_use_the_list_and_no_more_than_three_fifths_of_the_work_area()
    {
        var layout = TaskPreview.Measure(20, 1920, 1032);
        Assert.True(layout.Listed);
        Assert.Equal(1032 * 0.6 + 16, layout.Height);
        Assert.True(layout.Width <= 1920 - 32);
        Assert.Equal(layout, TaskPreview.Measure(20, 0, 0));
    }

    [Fact]
    public void An_app_that_is_not_running_has_no_size_until_its_name_is_measured()
    {
        Assert.Equal(default, TaskPreview.Measure(0, 1920, 1032));
    }

    [Fact]
    public void The_preview_sits_above_the_button_and_inside_the_work_area()
    {
        var work = new QRect(0, 0, 1920, 1080);
        var placed = TaskPreview.Place(new QRect(100, 1000, 40, 48), 200, 160, work);
        Assert.Equal(new QRect(20, 832, 200, 160), placed);

        Assert.Equal(0, TaskPreview.Place(new QRect(0, 1000, 40, 48), 200, 160, work).X);
        Assert.Equal(1720, TaskPreview.Place(new QRect(1900, 1000, 40, 48), 200, 160, work).X);
        Assert.Equal(0, TaskPreview.Place(new QRect(100, 20, 40, 48), 200, 160, work).Y);
    }

    [Fact]
    public void A_window_title_reaches_an_open_preview_without_replacing_the_window()
    {
        using var bar = new Bar();
        var window = new Win { Id = 1, Title = "Old", Icon = "old" };
        bar.Set(Task("notes", "Notes", window));
        bar.Preview.OpenFromKeyboard("notes");
        var raised = 0;
        bar.Preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskPreview.Windows))
                raised++;
        };

        window.Title = "New";
        window.Raise(nameof(ITaskWindow.Title));
        window.Icon = "new";
        window.Raise(nameof(ITaskWindow.Icon));
        window.Raise(nameof(ITaskWindow.Minimized));

        Assert.Equal(2, raised);
        Assert.Same(window, bar.Preview.Windows[0]);
        Assert.Equal("New", bar.Preview.Windows[0].Title);
    }

    [Fact]
    public void Enter_brings_a_window_forward_and_its_close_button_or_Delete_closes_it()
    {
        using var bar = new Bar();
        var first = new Win { Id = 1, Title = "One" };
        var second = new Win { Id = 2, Title = "Two" };
        bar.Set(Task("notes", "Notes", first, second));
        bar.Preview.OpenFromKeyboard("notes");
        Assert.True(bar.Preview.ActivateFocused());
        Assert.Equal(1, first.Activations);
        Assert.False(bar.Preview.IsOpen);
        Assert.Equal(0, bar.Dismissed);

        bar.Preview.OpenFromKeyboard("notes");
        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.ActivateFocused());
        Assert.Equal(1, first.Closes);
        Assert.Equal(1, first.Activations);
        Assert.True(bar.Preview.IsOpen);
        Assert.Contains(first, bar.Preview.Windows);

        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.Equal(second, bar.Preview.Focused);
        Assert.True(bar.Preview.PressDelete());
        Assert.Equal(1, second.Closes);
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal(second, bar.Preview.Windows[1]);

        // A fresh adapter for the same window compares equal, so the keyboard stays there until that window is gone.
        bar.Set(Task("notes", "Notes", new Win { Id = 1, Title = "One" }, new Win { Id = 2, Title = "Two" }));
        bar.Preview.Refresh();
        Assert.Equal(1, bar.Preview.FocusIndex);
        Assert.Equal(second, bar.Preview.Focused);
        Assert.False(bar.Preview.FocusClose);
        Assert.True(bar.Preview.Tab(true));
        bar.Preview.Refresh();
        Assert.Equal(1, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.FocusClose);

        bar.Set(Task("notes", "Notes", new Win { Id = 1, Title = "One" }));
        bar.Preview.Refresh();
        Assert.Equal(0, bar.Preview.FocusIndex);
        Assert.Equal(first, bar.Preview.Focused);
        Assert.False(bar.Preview.FocusClose);
        Assert.True(bar.Preview.IsOpen);
    }

    [Fact]
    public void The_keyboard_moves_through_cards_and_Escape_goes_back_to_the_button()
    {
        using var bar = new Bar();
        bar.Set(Task("notes", "Notes", new Win { Id = 1, Title = "One" }, new Win { Id = 2, Title = "Two" }, new Win { Id = 3, Title = "Three" }));
        var grabWhenOpened = false;
        var widthWhenOpened = 0d;
        bar.Preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(TaskPreview.IsOpen) || !bar.Preview.IsOpen)
                return;
            grabWhenOpened = bar.Preview.GrabFocus;
            widthWhenOpened = bar.Preview.Width;
        };
        bar.Preview.OpenFromKeyboard("notes");
        Assert.True(bar.Preview.IsOpen);
        Assert.True(grabWhenOpened);
        Assert.True(widthWhenOpened > 0);
        Assert.True(bar.Preview.ShowsClose(0));
        Assert.False(bar.Preview.ShowsClose(1));
        Assert.False(bar.Preview.Listed);
        Assert.False(bar.Preview.MoveVertical(1));

        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.Equal(0, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.Tab(true));
        Assert.Equal(1, bar.Preview.FocusIndex);
        Assert.False(bar.Preview.FocusClose);
        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.Equal(2, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.MoveHorizontal(1));
        Assert.Equal(2, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.ShowsClose(2));

        bar.Preview.Escape();
        Assert.False(bar.Preview.IsOpen);
        Assert.Equal(1, bar.Dismissed);
        Assert.Equal("notes", bar.Preview.TaskKey);

        bar.Open("notes");
        bar.Preview.Escape();
        Assert.False(bar.Preview.IsOpen);
        Assert.Equal(1, bar.Dismissed);
    }

    [Fact]
    public void Cards_and_a_list_keep_the_window_the_keyboard_is_on()
    {
        using var cards = new Bar();
        var one = new Win { Id = 1, Title = "One" };
        var two = new Win { Id = 2, Title = "Two" };
        var three = new Win { Id = 3, Title = "Three" };
        cards.Set(Task("notes", "Notes", one, two, three));
        cards.Preview.SetWorkArea(1280, 1032);
        cards.Preview.OpenFromKeyboard("notes");
        Assert.False(cards.Preview.Listed);
        Assert.True(cards.Preview.MoveHorizontal(1));
        Assert.True(cards.Preview.MoveHorizontal(1));
        Assert.True(cards.Preview.MoveHorizontal(1));
        Assert.Equal(1, cards.Preview.FocusIndex);
        Assert.True(cards.Preview.FocusClose);
        cards.Set(Task("notes", "Notes", [one, two, three, .. More(4, 6)]));
        cards.Preview.Refresh();
        Assert.True(cards.Preview.Listed);
        Assert.Equal(1, cards.Preview.FocusIndex);
        Assert.Equal(two, cards.Preview.Focused);
        Assert.False(cards.Preview.FocusClose);

        using var list = new Bar();
        var windows = More(1, 9);
        list.Set(Task("notes", "Notes", windows));
        list.Preview.SetWorkArea(1280, 1032);
        list.Preview.OpenFromKeyboard("notes");
        Assert.True(list.Preview.Listed);
        Assert.False(list.Preview.MoveHorizontal(1));
        Assert.True(list.Preview.MoveVertical(1));
        Assert.True(list.Preview.MoveVertical(1));
        Assert.True(list.Preview.Tab(true));
        Assert.Equal(2, list.Preview.FocusIndex);
        Assert.True(list.Preview.FocusClose);
        list.Set(Task("notes", "Notes", windows[0], windows[1], windows[2]));
        list.Preview.Refresh();
        Assert.False(list.Preview.Listed);
        Assert.Equal(windows[2], list.Preview.Focused);
        Assert.False(list.Preview.FocusClose);
    }

    [Fact]
    public void A_long_list_scrolls_the_focused_row_fully_into_view()
    {
        using var bar = new Bar();
        var windows = More(1, 30);
        bar.Set(Task("notes", "Notes", windows));
        bar.Preview.SetWorkArea(1920, 1032);
        bar.Preview.OpenFromKeyboard("notes");
        Assert.True(bar.Preview.Listed);

        Assert.True(bar.Preview.Tab(true));
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.Tab(true));
        Assert.Equal(1, bar.Preview.FocusIndex);
        Assert.False(bar.Preview.FocusClose);
        Assert.True(bar.Preview.Tab(false));
        Assert.Equal(0, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.Tab(false));
        Assert.Equal(0, bar.Preview.FocusIndex);
        Assert.False(bar.Preview.FocusClose);

        for (var i = 0; i < 29; i++)
            Assert.True(bar.Preview.MoveVertical(1));
        Assert.Equal(29, bar.Preview.FocusIndex);
        var viewport = Math.Min(30 * (double)TaskPreview.RowHeight, 1032 * 0.6);
        var scroll = 29 * (double)TaskPreview.RowHeight + TaskPreview.RowHeight - viewport;
        Assert.Equal(viewport, bar.Preview.ViewportHeight);
        Assert.Equal(scroll, bar.Preview.ScrollY);
        Assert.True(Math.Abs(bar.Preview.ScrollY - 580.8) < 0.5);
        var top = bar.Preview.FocusIndex * (double)TaskPreview.RowHeight;
        Assert.True(bar.Preview.ScrollY <= top + 0.5);
        Assert.True(bar.Preview.ScrollY + bar.Preview.ViewportHeight + 0.5 >= top + TaskPreview.RowHeight);

        Assert.True(bar.Preview.MoveVertical(1));
        Assert.Equal(29, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.Tab(true));
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.MoveVertical(1));
        Assert.Equal(29, bar.Preview.FocusIndex);
        Assert.True(bar.Preview.FocusClose);
        Assert.True(bar.Preview.ActivateFocused());
        Assert.Equal(1, windows[29].Closes);
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal(30, bar.Preview.Windows.Count);

        Assert.True(bar.Preview.MoveVertical(-1));
        Assert.Equal(29, bar.Preview.FocusIndex);
        Assert.False(bar.Preview.FocusClose);
        Assert.True(bar.Preview.MoveVertical(-1));
        Assert.Equal(28, bar.Preview.FocusIndex);
        Assert.False(bar.Preview.FocusClose);
    }

    [Fact]
    public void The_pointer_does_not_move_or_close_a_preview_the_keyboard_opened()
    {
        using var bar = new Bar();
        var notes = new Win { Id = 1, Title = "Notes" };
        bar.Set(Task("notes", "Notes", notes));
        bar.Set(Task("other", "Other", new Win { Id = 2, Title = "Other" }));
        bar.Preview.OpenFromKeyboard("notes");

        bar.Preview.LeaveButton("notes");
        bar.Preview.PointerInside(false);
        bar.Advance(TaskPreview.CloseDelayMs + TaskPreview.OpenDelayMs);
        Assert.True(bar.Preview.IsOpen);
        Assert.True(bar.Preview.GrabFocus);
        Assert.Equal(notes, bar.Preview.Windows[0]);

        bar.Preview.HoverButton("other");
        Assert.Equal("notes", bar.Preview.TaskKey);
        Assert.Equal(notes, bar.Preview.Windows[0]);
        Assert.True(bar.Preview.GrabFocus);
    }

    [Fact]
    public void The_windows_are_the_ones_the_app_has_when_the_preview_opens()
    {
        using var bar = new Bar();
        bar.Set(Task("notes", "Notes", new Win { Id = 1, Title = "Old" }));
        bar.Preview.HoverButton("notes");
        bar.Set(Task("notes", "Notes", new Win { Id = 2, Title = "Kept" }, new Win { Id = 3, Title = "Other" }));
        bar.Advance(TaskPreview.OpenDelayMs - 1);
        Assert.False(bar.Preview.IsOpen);
        bar.Advance(1);
        Assert.Equal(["Kept", "Other"], bar.Preview.Windows.Select(window => window.Title));
    }

    [Fact]
    public void A_preview_stays_while_its_app_does_and_shows_the_name_when_nothing_is_running()
    {
        using var bar = new Bar();
        var notes = new Win { Id = 1, Title = "Notes" };
        bar.Set(Task("notes", "Notes", notes));
        bar.Set(Task("other", "Other", new Win { Id = 2, Title = "Other" }));
        bar.Open("notes");
        bar.Set(Task("other", "Other", new Win { Id = 2, Title = "Other" }, new Win { Id = 4, Title = "More" }));
        bar.Preview.Refresh();
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal([notes], bar.Preview.Windows);

        bar.Set(Task("notes", "Notes", notes, new Win { Id = 5, Title = "Second" }));
        bar.Preview.Refresh();
        Assert.True(bar.Preview.IsOpen);
        Assert.Equal(2, bar.Preview.Windows.Count);

        bar.Set(Task("notes", "Notes"));
        bar.Preview.Refresh();
        Assert.True(bar.Preview.IsOpen);
        Assert.False(bar.Preview.Running);
        Assert.Equal(0, bar.Preview.Width);
        Assert.Equal(0, bar.Preview.Height);
        Assert.Equal("Notes", bar.Preview.Name);
        Assert.Equal(0, bar.Dismissed);
        Assert.Equal(0, bar.AnchorGone);

        bar.Preview.Hide();
        bar.Remove("notes");
        bar.Open("other");
        bar.Preview.HoverButton("notes");
        Assert.False(bar.Preview.IsOpen);
        Assert.Equal(0, bar.Dismissed);
        Assert.Equal(0, bar.AnchorGone);

        bar.Set(Task("pinned", "Notes"));
        var widthWhenOpened = -1d;
        bar.Preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskPreview.IsOpen) && bar.Preview.IsOpen)
                widthWhenOpened = bar.Preview.Width;
        };
        bar.Preview.HoverButton("pinned");
        bar.Advance(TaskPreview.OpenDelayMs);
        Assert.True(bar.Preview.IsOpen);
        Assert.False(bar.Preview.Running);
        Assert.Equal(0, widthWhenOpened);
        Assert.Equal(0, bar.Preview.Height);
        Assert.Equal("Notes", bar.Preview.Name);
    }

    [Fact]
    public void The_keyboard_hears_when_its_app_goes_and_when_its_last_window_closes()
    {
        using var gone = new Bar();
        gone.Set(Task("notes", "Notes", new Win { Id = 1, Title = "Notes" }));
        gone.Preview.OpenFromKeyboard("notes");
        gone.Remove("notes");
        gone.Preview.Refresh();
        Assert.False(gone.Preview.IsOpen);
        Assert.Equal(1, gone.AnchorGone);
        Assert.Equal(0, gone.Dismissed);
        Assert.Equal("notes", gone.Preview.TaskKey);

        using var pinned = new Bar();
        pinned.Set(Task("notes", "Notes", new Win { Id = 1, Title = "Notes" }));
        pinned.Preview.OpenFromKeyboard("notes");
        pinned.Set(Task("notes", "Notes"));
        pinned.Preview.Refresh();
        Assert.False(pinned.Preview.IsOpen);
        Assert.Equal(1, pinned.Dismissed);
        Assert.Equal(0, pinned.AnchorGone);
        Assert.Equal("notes", pinned.Preview.TaskKey);
    }

    [Fact]
    public void Up_on_an_app_that_is_not_running_does_not_open_its_preview()
    {
        using var bar = new Bar();
        bar.Set(Task("notes", "Notes"));
        bar.Preview.OpenFromKeyboard("notes");
        bar.Advance(TaskPreview.OpenDelayMs);
        Assert.False(bar.Preview.IsOpen);
        Assert.Equal(0, bar.Dismissed);
        Assert.Equal(0, bar.AnchorGone);
    }

    private static TaskItem Task(string key, string name, params ITaskWindow[] windows) =>
        new(key, name, new App(name), windows, "", name);

    private static Win[] More(int first, int count) =>
        [.. Enumerable.Range(first, count).Select(id => new Win { Id = id, Title = "W" + id })];

    private sealed record App(string Name) : ITaskApp
    {
        public string Id => Name;

        public string Icon => "";

        public string AppId => Name;
    }

    /// <summary>Equal when it is the same window, whichever adapter is reading it. That is how <see cref="TaskWindow"/> compares.</summary>
    private sealed class Win : IOpenWindow
    {
        public int Id { get; init; }

        public string AppId { get; set; } = "app";

        public string Executable { get; set; } = @"C:\app.exe";

        public string Title { get; set; } = "";

        public string Icon { get; set; } = "";

        public bool Activated { get; set; }

        public bool Minimized { get; set; }

        public bool Attention { get; set; }

        public IReadOnlyList<ScreenInfo> Screens => [];

        public int Activations { get; private set; }

        public int Closes { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Activate() => Activations++;

        public void Minimize() => Minimized = true;

        public void Restore() => Minimized = false;

        public void Close() => Closes++;

        public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override bool Equals(object? obj) => obj is Win other && other.Id == Id;

        public override int GetHashCode() => Id;
    }

    private sealed class Bar : IDisposable
    {
        private readonly Dictionary<string, TaskItem> _tasks = [];
        private readonly FakeTimeProvider _time = new();

        public Bar()
        {
            Preview = new TaskPreview(Lookup, _time);
            Preview.Dismissed += (_, _) => Dismissed++;
            Preview.AnchorGone += (_, _) => AnchorGone++;
        }

        public TaskPreview Preview { get; }

        public int Dismissed { get; private set; }

        public int AnchorGone { get; private set; }

        public void Set(TaskItem task) => _tasks[task.Key] = task;

        public void Remove(string key) => _tasks.Remove(key);

        public void Open(string key)
        {
            Preview.HoverButton(key);
            Advance(TaskPreview.OpenDelayMs);
        }

        public void Advance(int milliseconds) => _time.Advance(TimeSpan.FromMilliseconds(milliseconds));

        public void Dispose() => Preview.Dispose();

        private TaskItem? Lookup(string key) => _tasks.TryGetValue(key, out var task) ? task : null;
    }
}
