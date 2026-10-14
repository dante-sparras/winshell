namespace Winshell.Core;

/// <summary>What the floating pill at the bottom of the screen is doing.</summary>
public enum PillState
{
    /// <summary>Not on screen.</summary>
    Hidden,

    /// <summary>Live input: the level bars move with <see cref="IPluginContext.FeedPill"/>, and a clock counts up.</summary>
    Live,

    /// <summary>Work in progress: a ring spins and the bars swell.</summary>
    Working,

    /// <summary>A check. The pill hides itself shortly after.</summary>
    Done,

    /// <summary>A caution glyph and <see cref="PillFace.Message"/>. The pill hides itself after a few seconds.</summary>
    Failed,
}

/// <summary>
/// The pill a plugin shows through <see cref="IPluginContext.ShowPill"/>. The host draws it, so it matches the bar.
/// It never takes focus. One plugin shows it at a time: the latest call wins.
/// </summary>
public sealed record PillFace
{
    /// <summary>Nothing on screen.</summary>
    public static PillFace Hidden { get; } = new();

    /// <summary>What the pill is doing.</summary>
    public PillState State { get; init; }

    /// <summary>Named icon for <see cref="PillState.Live"/>, such as <c>mic</c>.</summary>
    public string Glyph { get; init; } = "mic";

    /// <summary>True during <see cref="PillState.Live"/> shows a cancel and a stop button in place of the glyph.</summary>
    public bool Controls { get; init; }

    /// <summary>What a screen reader announces.</summary>
    public string Label { get; init; } = "";

    /// <summary>The reason shown with <see cref="PillState.Failed"/>.</summary>
    public string Message { get; init; } = "";

    /// <summary>The plugin command the cancel button sends. Empty hides the button.</summary>
    public string Cancel { get; init; } = "";

    /// <summary>The plugin command the stop button sends. Empty hides the button.</summary>
    public string Stop { get; init; } = "";
}
