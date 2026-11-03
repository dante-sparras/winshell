namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// Quick settings for one taskbar. The tiles, the sliders and the pages are shared. Each bar has its own
/// <see cref="QuickSettingsPopup"/>.
/// </summary>
public sealed class QuickSettingsPopup : ObservableObject
{
    private readonly QuickSettings _settings;

    internal QuickSettingsPopup(QuickSettings settings) => _settings = settings;

    public QuickSettings Settings => _settings;

    public string Page
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;
            Changed(nameof(ShowingTiles));
            Changed(nameof(ShowingPowerMode));
            Changed(nameof(ShowingOutput));
            Changed(nameof(ShowingVolume));
            Changed(nameof(ShowingAdd));
        }
    } = "";

    public bool IsOpen { get; private set => Set(ref field, value); }

    /// <summary>The tiles can be unpinned, added and reordered. Opening or closing the flyout leaves this.</summary>
    public bool Editing
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;
            if (!value)
                ClearDrag();
            Changed(nameof(Browsing));
        }
    }

    /// <summary>The footer shows the battery, edit and settings while the tiles are not being edited.</summary>
    public bool Browsing => !Editing;

    public bool ShowingTiles => Page.Length == 0;

    public bool ShowingPowerMode => Page == "powerMode";

    public bool ShowingOutput => Page == "output";

    public bool ShowingVolume => Page == "volume";

    public bool ShowingAdd => Page == "add";

    /// <summary>The tile a drag picked up, as an index into <see cref="QuickSettingsPins.Shown"/>, or -1.</summary>
    public int DragIndex { get; private set => Set(ref field, value); } = -1;

    /// <summary>Where the dragged tile would land, as an index into <see cref="QuickSettingsPins.Shown"/>, or -1.</summary>
    public int DropIndex { get; private set => Set(ref field, value); } = -1;

    /// <summary>The tile the keyboard should be on after a move or an unpin, or empty.</summary>
    public string FocusKey { get; private set => Set(ref field, value); } = "";

    /// <summary>Shows the tiles. Opening one that was on a page comes back to them.</summary>
    public void Show()
    {
        Page = "";
        if (IsOpen)
            return;
        Editing = false;
        IsOpen = true;
        _settings.Opened();
    }

    public void Hide()
    {
        Page = "";
        Editing = false;
        if (!IsOpen)
            return;
        IsOpen = false;
        _settings.Closed();
    }

    /// <summary>Leaves a page, or edit mode when the tiles are showing. False means the flyout itself closes.</summary>
    public bool Escape()
    {
        if (Page.Length > 0)
        {
            Page = "";
            return true;
        }
        if (Editing)
        {
            Editing = false;
            return true;
        }
        return false;
    }

    public void Edit()
    {
        Page = "";
        Editing = true;
    }

    public void Done()
    {
        Page = "";
        Editing = false;
    }

    public void ShowAdd() => Page = "add";

    /// <summary>Pins a tile at the end and returns to the tiles.</summary>
    public void Add(string key)
    {
        _settings.Pins.Add(key);
        Page = "";
    }

    public void Activate(QuickSettingsTile tile)
    {
        // A tile does nothing while it is being edited. The drag surface takes the press.
        if (Editing)
            return;
        if (tile.Page.Length > 0)
            Page = tile.Page;
        else
            _settings.Open(tile.SettingsUri, this);
    }

    public void Back() => Page = "";

    public void ShowOutput() => Page = "output";

    public void ShowVolume() => Page = "volume";

    public void OpenBattery() => _settings.Open("ms-settings:batterysaver", this);

    public void OpenAllSettings() => _settings.Open("ms-settings:", this);

    public void OpenVolumeSettings() => _settings.Open("ms-settings:apps-volume", this);

    /// <summary>Moves the tile by an offset among the tiles on show, and keeps the keyboard on it.</summary>
    public void MoveBy(string key, int offset) => AskFocus(_settings.Pins.MoveBy(key, offset));

    /// <summary>Unpins a tile. From the keyboard, the focus goes to the tile that takes its place.</summary>
    public void Unpin(string key, bool fromKeyboard)
    {
        if (_settings.Pins.Unpin(key, fromKeyboard) is { } focus)
            AskFocus(focus);
    }

    public void BeginDrag(QuickSettingsTile tile)
    {
        var index = _settings.Pins.Shown.IndexOf(tile);
        DragIndex = index;
        DropIndex = index;
    }

    public void DragTo(double x, double y, double tileHeight)
    {
        if (DragIndex < 0)
            return;
        DropIndex = IndexAt(x, y, _settings.Pins.Shown.Count, tileHeight);
    }

    /// <summary>Drops the tile. The order changes when it lands on another tile.</summary>
    public void EndDrag()
    {
        var from = DragIndex;
        var to = DropIndex;
        var shown = _settings.Pins.Shown;
        var key = from >= 0 && from < shown.Count ? shown[from].Key : null;
        var target = to >= 0 && to < shown.Count ? shown[to].Key : null;
        ClearDrag();
        if (key is not null && target is not null && from != to)
            _settings.Pins.Move(key, target);
    }

    /// <summary>Where a point in the tile grid lands, with 12 between the tile buttons.</summary>
    public static int IndexAt(double x, double y, int count, double tileHeight)
    {
        if (count <= 0)
            return -1;
        const double tileWidth = 96;
        const double spacing = 12;
        var column = Math.Clamp((int)Math.Floor((x + spacing / 2) / (tileWidth + spacing)), 0, QuickSettings.TileColumns - 1);
        var row = Math.Max(0, (int)Math.Floor((y + spacing / 2) / (tileHeight + spacing)));
        return Math.Min(count - 1, row * QuickSettings.TileColumns + column);
    }

    /// <summary>How dim a tile is while another drag holds it. The one being dragged is half shown.</summary>
    public double DragOpacity(int index) => DragIndex == index ? 0.5 : 1;

    /// <summary>Where a dragged tile would land: -1 before this tile, 1 after it, 0 not here.</summary>
    public int DropSide(int index) =>
        DragIndex < 0 || DropIndex != index || DragIndex == index ? 0 : DropIndex < DragIndex ? -1 : 1;

    private void ClearDrag()
    {
        DragIndex = -1;
        DropIndex = -1;
    }

    private void AskFocus(string key)
    {
        // The same tile can move twice. The view still has to focus the new one.
        if (FocusKey == key)
            Changed(nameof(FocusKey));
        else
            FocusKey = key;
    }
}
