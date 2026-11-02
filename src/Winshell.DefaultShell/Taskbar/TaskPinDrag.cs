namespace Winshell.DefaultShell.Taskbar;

/// <summary>One pinned app dragged onto another. A press that stays put is still a click. Shift does not drag.</summary>
public sealed class TaskPinDrag
{
    private int _drag = -1;
    private int _drop = -1;

    /// <summary>The pinned button being dragged, or -1.</summary>
    public int DragIndex => _drag;

    /// <summary>The pinned button it would land on, or -1.</summary>
    public int DropIndex => _drop;

    /// <summary>A drag is in progress. The hover preview does not open.</summary>
    public bool Active => _drag >= 0;

    /// <summary>The pointer has moved onto another pin. The dragged button draws faded, and the release is not a click.</summary>
    public bool Moved => Active && _drop != _drag;

    /// <summary>The release reorders, so the button's click does nothing.</summary>
    public bool SuppressClick { get; private set; }

    /// <summary>The pin under a point in the row. Pins are the buttons at the start, each the same width. -1 when there is none.</summary>
    public static int IndexAt(double x, int pinnedCount)
    {
        if (pinnedCount < 1)
            return -1;
        var at = (int)Math.Floor((x + TaskStrip.Spacing / 2) / (TaskStrip.Button + TaskStrip.Spacing));
        return Math.Clamp(at, 0, pinnedCount - 1);
    }

    /// <summary>Where the drop line sits in the target button's coordinates: before it when moving back, after it when moving on.</summary>
    public static double MarkerOffset(double buttonWidth, bool before) => before ? -7 : buttonWidth + 5;

    /// <summary>A left press on a pinned app, without Shift. Anything else is not a drag.</summary>
    public bool Press(int index, bool pinned, bool leftButton, bool shift)
    {
        if (!pinned || !leftButton || shift || index < 0)
            return false;
        _drag = _drop = index;
        return true;
    }

    /// <summary>The pointer moved. True when it has landed on a different pin, so the preview closes.</summary>
    public bool Move(double xInRow, int pinnedCount)
    {
        if (_drag < 0)
            return false;
        var at = IndexAt(xInRow, pinnedCount);
        if (at < 0 || at == _drop)
            return false;
        _drop = at;
        // Back on the pin it started on, the release is a click again.
        SuppressClick = at != _drag;
        return SuppressClick;
    }

    /// <summary>The press ended. A landing on another pin is a move of those two ids. A press that stayed put is not.</summary>
    public PinMove? Release(IReadOnlyList<TaskItem> tasks)
    {
        if (_drag < 0)
            return null;
        var from = _drag;
        var to = _drop;
        _drag = _drop = -1;
        if ((uint)from >= (uint)tasks.Count || (uint)to >= (uint)tasks.Count)
            return null;
        var source = tasks[from].Pin;
        var target = tasks[to].Pin;
        if (from == to || source.Length == 0 || target.Length == 0)
        {
            SuppressClick = false;
            return null;
        }
        SuppressClick = true;
        return new PinMove(source, target);
    }

    /// <summary>The press was cancelled. Nothing moves, and the click is not swallowed.</summary>
    public void Cancel()
    {
        _drag = _drop = -1;
        SuppressClick = false;
    }

    /// <summary>The click that followed a reorder has been swallowed.</summary>
    public void ClearSuppress() => SuppressClick = false;
}

/// <summary>The pin that moved, and the pin it landed on. The pins list puts it before that one when moving back, and after it when moving on.</summary>
public readonly record struct PinMove(string Id, string Target);
