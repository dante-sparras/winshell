namespace Winshell.DefaultShell;

/// <summary>What a hotkey reports to its card: whether Windows took it.</summary>
public readonly record struct SettingsHotkeyState(string Error, bool Taken, bool Active);

/// <summary>
/// What the Hotkeys page does with a sequence. Empty turns one off. A sequence that would take a typing key, or that
/// repeats the other hotkey, is refused and not saved. <see cref="Preferences.HotkeyRefusal"/> is the typing rule.
/// </summary>
public static class SettingsHotkeys
{
    /// <summary>The sequence a committed field keeps, and why the typed one was refused when it was not saved.</summary>
    public readonly record struct SettingsHotkeyCommit(string Sequence, string Refused);

    /// <summary>
    /// The sequence to save after the field is committed. A refused one is not saved. Empty turns the hotkey off.
    /// </summary>
    public static SettingsHotkeyCommit? Commit(string typed, string saved, string otherTitle, string otherSequence)
    {
        var next = typed.Trim();
        if (next == saved)
            return null;
        var refused = Refusal(next, otherTitle, otherSequence);
        return refused.Length == 0 ? new SettingsHotkeyCommit(next, "") : new SettingsHotkeyCommit(saved, refused);
    }

    public static string Refusal(string text, string otherTitle, string otherSequence)
    {
        var why = Preferences.HotkeyRefusal(text);
        if (text.Length == 0 || why.Length > 0)
            return why;
        if (otherSequence.Length > 0 && Same(text, otherSequence))
            return otherTitle + " already uses " + otherSequence;
        return "";
    }

    public static string Description(string refused, string sequence, SettingsHotkeyState hotkey)
    {
        if (refused.Length > 0)
            return refused;
        if (sequence.Length == 0)
            return "Off";
        if (hotkey.Error.Length > 0)
            return hotkey.Error;
        if (hotkey.Taken)
            return "Windows or another app is using " + sequence;
        if (hotkey.Active)
            return "On";
        return "";
    }

    /// <summary>The same chords, ignoring case and the spaces around a key.</summary>
    public static bool Same(string left, string right) => Normalize(left) == Normalize(right);

    public static string Normalize(string text)
    {
        var chords = text.ToLowerInvariant().Split(',');
        for (var i = 0; i < chords.Length; i++)
        {
            var parts = chords[i].Split('+');
            for (var p = 0; p < parts.Length; p++)
                parts[p] = parts[p].Trim();
            chords[i] = string.Join('+', parts);
        }
        return string.Join(',', chords);
    }
}
