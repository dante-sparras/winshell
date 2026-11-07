namespace Winshell;

/// <summary>The edges of a screen or a window, such as the ones a panel is anchored to. These values combine.</summary>
[Flags]
public enum Edges
{
    /// <summary>No edge.</summary>
    None = 0,

    /// <summary>The top edge.</summary>
    Top = 1,

    /// <summary>The left edge.</summary>
    Left = 2,

    /// <summary>The right edge.</summary>
    Right = 4,

    /// <summary>The bottom edge.</summary>
    Bottom = 8
}
