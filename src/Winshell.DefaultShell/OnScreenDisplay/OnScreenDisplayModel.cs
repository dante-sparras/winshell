using System.Globalization;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The Windows 11 on-screen display: a short panel centered on the bottom of the work area, 8 logical pixels
/// above the taskbar. Volume, brightness and microphone mute stay for 3 seconds; a media change stays for 5.
/// The pointer holding it keeps it open. It never writes volume, brightness or mute. Media buttons call the session.
/// The first reading after startup is the baseline and does not show it. One panel: brightness uses that monitor,
/// and anything else uses the primary screen.
/// </summary>
public sealed class OnScreenDisplayModel : ObservableObject
{
    private readonly SynchronizationContext? _ui;
    private readonly ITimer _hide;
    private readonly ITimer _fade;
    private readonly ITimer _settle;
    private IReadOnlyList<ScreenInfo> _screens = [];
    private ScreenInfo? _brightnessSource;
    private ScreenInfo? _brightnessScreen;
    private bool _settled;
    private string? _volumeKey;
    private string? _brightnessKey;
    private string? _microphoneKey;
    private string? _mediaKey;

    public OnScreenDisplayModel(TimeProvider time)
    {
        _ui = SynchronizationContext.Current;
        _hide = time.CreateTimer(_ => OnUi(OnHide), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _fade = time.CreateTimer(_ => OnUi(OnFade), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _settle = time.CreateTimer(_ => OnUi(SettleNow), null, TimeSpan.FromMilliseconds(SettleMs), Timeout.InfiniteTimeSpan);
        Refresh();
    }

    public const int PanelWidth = 360;

    public const int BottomGap = 8;

    public const int LevelHideMs = 3000;

    public const int MediaHideMs = 5000;

    public const int SettleMs = 400;

    /// <summary>The fast control animation, in milliseconds. With animation effects off the fade is skipped.</summary>
    public const int FadeMs = 167;

    public const string ArtGlyph = "\uE8D6";

    public const string PreviousGlyph = "\uE892";

    public const string NextGlyph = "\uE893";

    /// <summary>Volume, brightness, microphone or media. Empty until something is shown.</summary>
    public string Mode { get; private set => Set(ref field, value); } = "";

    public bool Open { get; private set => Set(ref field, value); }

    public bool Hiding { get; private set => Set(ref field, value); }

    public bool PointerInside
    {
        get;
        set
        {
            if (Set(ref field, value))
                OnPointer();
        }
    }

    public bool AnimationsEnabled { get; set => Set(ref field, value); } = true;

    public string Announcement { get; private set => Set(ref field, value); } = "";

    public bool ShowMedia { get; private set => Set(ref field, value); }

    public bool ShowLevel { get; private set => Set(ref field, value); }

    public bool ShowMicrophone { get; private set => Set(ref field, value); }

    public bool ShowArtist { get; private set => Set(ref field, value); }

    public bool HasArt { get; private set => Set(ref field, value); }

    public int PanelHeight { get; private set => Set(ref field, value); } = 64;

    public double Level { get; private set => Set(ref field, value); }

    public double LevelMinimum { get; private set => Set(ref field, value); }

    public double LevelMaximum { get; private set => Set(ref field, value); } = 1;

    public string LevelName { get; private set => Set(ref field, value); } = "Volume";

    public string LevelGlyph { get; private set => Set(ref field, value); } = "";

    public string MicrophoneGlyph { get; private set => Set(ref field, value); } = "\uE720";

    public string MicrophoneText { get; private set => Set(ref field, value); } = "Microphone on";

    public string PlayGlyph { get; private set => Set(ref field, value); } = "\uE768";

    public string PlayName { get; private set => Set(ref field, value); } = "Play";

    public string MediaHeading { get; private set => Set(ref field, value); } = "Media";

    /// <summary>The screen the panel is on. Brightness uses that monitor; everything else uses the primary.</summary>
    public ScreenInfo? Screen { get; private set => Set(ref field, value); }

    public double Volume
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _volumeKey, VolumeKey(), VolumeKnown, "volume");
        }
    }

    public bool PlaybackMuted
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _volumeKey, VolumeKey(), VolumeKnown, "volume");
        }
    }

    public bool VolumeKnown
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _volumeKey, VolumeKey(), VolumeKnown, "volume");
        }
    }

    public int Brightness
    {
        get;
        private set
        {
            if (Set(ref field, value))
                Refresh();
        }
    }

    public int BrightnessMinimum
    {
        get;
        private set
        {
            if (Set(ref field, value))
                Refresh();
        }
    }

    public int BrightnessMaximum
    {
        get;
        private set
        {
            if (Set(ref field, value))
                Refresh();
        }
    } = 100;

    public bool BrightnessKnown
    {
        get;
        private set
        {
            if (Set(ref field, value))
                Refresh();
        }
    }

    public bool MicrophoneMuted
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _microphoneKey, MicrophoneKey(), MicrophoneKnown, "microphone");
        }
    }

    public bool MicrophoneKnown
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _microphoneKey, MicrophoneKey(), MicrophoneKnown, "microphone");
        }
    }

    public string MediaTitle
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _mediaKey, MediaKey(), MediaKnown, "media");
        }
    } = "";

    public string MediaArtist
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _mediaKey, MediaKey(), MediaKnown, "media");
        }
    } = "";

    public string MediaArtUrl
    {
        get;
        set
        {
            if (Set(ref field, value))
                Refresh();
        }
    } = "";

    public bool MediaPlaying
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _mediaKey, MediaKey(), MediaKnown, "media");
        }
    }

    public bool MediaKnown
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            Refresh();
            Note(ref _mediaKey, MediaKey(), MediaKnown, "media");
        }
    }

    public bool CanTogglePlayback { get; set => Set(ref field, value); }

    public bool CanGoNext { get; set => Set(ref field, value); }

    public bool CanGoPrevious { get; set => Set(ref field, value); }

    public event Action? TogglePlaybackRequested;

    public event Action? NextRequested;

    public event Action? PreviousRequested;

    /// <summary>The screens, primary first. A later list is read again, because a screen can be a new record.</summary>
    public void SetScreens(IReadOnlyList<ScreenInfo> screens)
    {
        _screens = screens;
        _brightnessScreen = CurrentBrightnessScreen();
        Refresh();
    }

    /// <summary>
    /// One monitor's reading, from a host that watches several. A change shows even when it lands on the level another
    /// monitor published last; a reading that is not a change, such as a monitor that appears, is only its baseline.
    /// </summary>
    public void PublishBrightness(ScreenInfo? screen, int minimum, int maximum, int level, bool changed)
    {
        _brightnessSource = screen;
        BrightnessMinimum = minimum;
        BrightnessMaximum = maximum;
        Brightness = level;
        BrightnessKnown = true;
        if (!_settled)
            return;
        var now = BrightnessKey();
        if (!changed)
        {
            _brightnessKey = now;
            return;
        }
        _brightnessScreen = CurrentBrightnessScreen();
        _brightnessKey = now;
        Show("brightness");
    }

    public void TogglePlayback()
    {
        if (CanTogglePlayback)
            TogglePlaybackRequested?.Invoke();
    }

    public void Next()
    {
        if (CanGoNext)
            NextRequested?.Invoke();
    }

    public void Previous()
    {
        if (CanGoPrevious)
            PreviousRequested?.Invoke();
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    private void SettleNow()
    {
        if (VolumeKnown)
            _volumeKey = VolumeKey();
        if (BrightnessKnown)
        {
            _brightnessKey = BrightnessKey();
            _brightnessScreen = CurrentBrightnessScreen();
        }
        if (MicrophoneKnown)
            _microphoneKey = MicrophoneKey();
        if (MediaKnown)
            _mediaKey = MediaKey();
        _settled = true;
        Refresh();
    }

    /// <summary>A null key is the baseline. A later different key shows <paramref name="kind"/>.</summary>
    private void Note(ref string? seen, string now, bool known, string kind)
    {
        if (!_settled || !known)
            return;
        if (seen is null)
        {
            seen = now;
            return;
        }
        if (seen == now)
            return;
        seen = now;
        Show(kind);
    }

    private string VolumeKey() => Volume.ToString("G17", CultureInfo.InvariantCulture) + "\n" + (PlaybackMuted ? "1" : "0");

    private string BrightnessKey() => Brightness.ToString(CultureInfo.InvariantCulture);

    private string MicrophoneKey() => MicrophoneMuted ? "1" : "0";

    private string MediaKey() =>
        MediaTitle.Length.ToString(CultureInfo.InvariantCulture) + ":" + MediaTitle
        + MediaArtist.Length.ToString(CultureInfo.InvariantCulture) + ":" + MediaArtist
        + (MediaPlaying ? "1" : "0");

    private void Show(string kind)
    {
        Hiding = false;
        Stop(_fade);
        Mode = kind;
        UpdateAnnouncement();
        Refresh();
        Open = true;
        if (PointerInside)
            Stop(_hide);
        else
            Arm();
    }

    private void OnPointer()
    {
        if (!Open)
            return;
        if (PointerInside)
        {
            Stop(_hide);
            if (Hiding)
            {
                Hiding = false;
                Stop(_fade);
            }
        }
        else
        {
            Arm();
        }
    }

    private void OnHide()
    {
        if (!Open || PointerInside)
            return;
        Hiding = true;
        Stop(_hide);
        if (!AnimationsEnabled)
            Open = false;
        else
            _fade.Change(TimeSpan.FromMilliseconds(FadeMs), Timeout.InfiniteTimeSpan);
    }

    private void OnFade()
    {
        if (Hiding)
            Open = false;
    }

    private void Arm()
    {
        var ms = Mode == "media" ? MediaHideMs : LevelHideMs;
        _hide.Change(TimeSpan.FromMilliseconds(ms), Timeout.InfiniteTimeSpan);
    }

    private static void Stop(ITimer timer) => timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private void UpdateAnnouncement()
    {
        if (Mode == "volume")
        {
            var text = PlaybackMuted ? "Volume muted" : "Volume " + Percent(Volume, 0, 1) + " percent";
            if (MediaKnown)
                text += ". " + MediaLine();
            Announcement = text;
        }
        else if (Mode == "brightness")
        {
            Announcement = "Brightness " + Percent(Brightness, BrightnessMinimum, BrightnessMaximum) + " percent";
        }
        else if (Mode == "microphone")
        {
            Announcement = MicrophoneMuted ? "Microphone muted" : "Microphone on";
        }
        else if (Mode == "media")
        {
            Announcement = MediaLine();
        }
    }

    private string MediaLine()
    {
        var state = MediaPlaying ? "Playing" : "Paused";
        var who = MediaTitle;
        if (MediaArtist.Length > 0)
            who = who.Length > 0 ? who + ", " + MediaArtist : MediaArtist;
        return who.Length > 0 ? state + " " + who : state;
    }

    private static int Percent(double value, double from, double to)
    {
        if (to == from)
            return 0;
        var percent = (int)Math.Round((value - from) / (to - from) * 100, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>The screen from the last reading, as the current list has it. A missing screen uses the primary.</summary>
    private ScreenInfo? CurrentBrightnessScreen()
    {
        var primary = _screens.Count > 0 ? _screens[0] : null;
        if (_brightnessSource is null)
            return primary;
        return _screens.FirstOrDefault(screen => screen.Id == _brightnessSource.Id) ?? primary;
    }

    private void Refresh()
    {
        ShowMedia = (Mode is "media" or "volume") && MediaKnown;
        ShowLevel = Mode is "brightness" or "volume" || (Mode == "media" && VolumeKnown);
        ShowMicrophone = Mode == "microphone";
        ShowArtist = MediaArtist.Length > 0;
        HasArt = MediaArtUrl.Length > 0;
        PanelHeight = ShowMedia && ShowLevel ? 120 : ShowMedia ? 80 : 64;
        Level = Mode == "brightness" ? Brightness : Volume;
        LevelMinimum = Mode == "brightness" ? BrightnessMinimum : 0;
        LevelMaximum = Mode == "brightness" ? BrightnessMaximum : 1;
        LevelName = Mode == "brightness" ? "Brightness" : "Volume";
        LevelGlyph = Mode == "brightness" ? "\uE706" : StatusGlyphs.VolumeGlyph(PlaybackMuted, Volume);
        MicrophoneGlyph = MicrophoneMuted ? "\uF781" : "\uE720";
        MicrophoneText = MicrophoneMuted ? "Microphone muted" : "Microphone on";
        PlayGlyph = MediaPlaying ? "\uE769" : "\uE768";
        PlayName = MediaPlaying ? "Pause" : "Play";
        MediaHeading = MediaTitle.Length > 0 ? MediaTitle : "Media";
        var fallback = _screens.Count > 0 ? _screens[0] : null;
        Screen = Mode == "brightness" && _brightnessScreen is not null ? _brightnessScreen : fallback;
    }
}
