namespace Winshell.Core;

/// <summary>What a stop asks of Winland. The shell and the desktop stop different things (ADR 0067).</summary>
public static class DeskStop
{
    /// <summary>
    /// The command a stop sends to Winland, or null when Winland stays up.
    /// The shell's own stop leaves the window manager running and writes only the shell's lifetime file.
    /// A desktop stop asks Winland to stop, and a desktop replace asks it to leave for the next build.
    /// </summary>
    /// <param name="desktop">True when the whole desktop is stopping, as <c>winoma stop</c> does.</param>
    /// <param name="replaced">True when another build takes this one's place.</param>
    /// <returns><c>stop</c>, <c>replace</c>, or null.</returns>
    public static string? WinlandCommand(bool desktop, bool replaced) =>
        desktop ? replaced ? "replace" : "stop" : null;
}
