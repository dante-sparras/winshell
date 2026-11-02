using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskPinDragTests
{
    private static TaskItem Pin(string id) => new("app:" + id, id, null, [], "", id);

    private static TaskItem Running(string key) => new(key, "", null, [], "", key);

    [Fact]
    public void Only_a_left_press_on_a_pinned_app_without_Shift_drags()
    {
        var drag = new TaskPinDrag();

        Assert.False(drag.Press(0, pinned: false, leftButton: true, shift: false));
        Assert.False(drag.Press(0, pinned: true, leftButton: true, shift: true));
        Assert.False(drag.Press(0, pinned: true, leftButton: false, shift: false));
        Assert.False(drag.Press(-1, pinned: true, leftButton: true, shift: false));
        Assert.False(drag.Active);

        Assert.True(drag.Press(1, pinned: true, leftButton: true, shift: false));
        Assert.Equal(1, drag.DragIndex);
        Assert.Equal(1, drag.DropIndex);
        Assert.False(drag.Moved);
        Assert.False(drag.SuppressClick);
    }

    [Fact]
    public void A_press_that_stays_put_is_still_a_click()
    {
        var drag = new TaskPinDrag();
        var tasks = new TaskItem[] { Pin("explorer"), Pin("edge"), Running("tool") };
        Assert.True(drag.Press(0, pinned: true, leftButton: true, shift: false));
        drag.Move(TaskStrip.ButtonX(0) + 10, pinnedCount: 2);

        var move = drag.Release(tasks);

        Assert.Null(move);
        Assert.False(drag.SuppressClick);
        Assert.False(drag.Active);
    }

    [Fact]
    public void Landing_on_another_pin_reorders_and_is_not_a_click_and_an_unpinned_app_is_not_a_target()
    {
        var drag = new TaskPinDrag();
        var tasks = new TaskItem[] { Pin("explorer"), Pin("edge"), Pin("store"), Running("tool") };
        Assert.True(drag.Press(0, pinned: true, leftButton: true, shift: false));

        Assert.False(drag.Move(TaskStrip.ButtonX(0) + 8, 3));
        Assert.True(drag.Move(TaskStrip.ButtonX(2) + 10, 3));
        Assert.True(drag.Moved);
        Assert.True(drag.SuppressClick);
        Assert.Equal(2, drag.DropIndex);
        Assert.Equal(TaskStrip.Button + 5, TaskPinDrag.MarkerOffset(TaskStrip.Button, before: false));

        var forward = drag.Release(tasks);
        Assert.Equal(new PinMove("explorer", "store"), forward);
        Assert.True(drag.SuppressClick);
        drag.ClearSuppress();
        Assert.False(drag.SuppressClick);

        Assert.True(drag.Press(2, pinned: true, leftButton: true, shift: false));
        Assert.True(drag.Move(TaskStrip.ButtonX(1) + 4, 3));
        Assert.Equal(-7, TaskPinDrag.MarkerOffset(TaskStrip.Button, before: true));
        Assert.Equal(new PinMove("store", "edge"), drag.Release(tasks));
    }

    [Fact]
    public void Dragging_off_a_pin_and_back_onto_it_is_still_a_click()
    {
        var drag = new TaskPinDrag();
        var tasks = new TaskItem[] { Pin("explorer"), Pin("edge") };
        Assert.True(drag.Press(0, pinned: true, leftButton: true, shift: false));
        Assert.True(drag.Move(TaskStrip.ButtonX(1) + 4, 2));
        Assert.True(drag.SuppressClick);

        Assert.False(drag.Move(TaskStrip.ButtonX(0) + 4, 2));
        Assert.False(drag.Moved);
        Assert.False(drag.SuppressClick);
        Assert.Null(drag.Release(tasks));
        Assert.False(drag.SuppressClick);
    }

    [Fact]
    public void The_pin_under_the_pointer_stays_inside_the_pinned_apps()
    {
        Assert.Equal(-1, TaskPinDrag.IndexAt(0, 0));
        Assert.Equal(0, TaskPinDrag.IndexAt(0, 3));
        Assert.Equal(0, TaskPinDrag.IndexAt(20, 3));
        Assert.Equal(1, TaskPinDrag.IndexAt(44, 3));
        Assert.Equal(2, TaskPinDrag.IndexAt(500, 3));
    }

    [Fact]
    public void Cancelling_drops_the_drag_and_an_app_that_is_not_pinned_does_not_move()
    {
        var drag = new TaskPinDrag();
        Assert.True(drag.Press(0, pinned: true, leftButton: true, shift: false));
        drag.Move(TaskStrip.ButtonX(1), 2);
        drag.Cancel();

        Assert.False(drag.Active);
        Assert.Null(drag.Release([Pin("explorer"), Pin("edge")]));
        Assert.False(drag.Press(2, pinned: false, leftButton: true, shift: false));
    }
}
