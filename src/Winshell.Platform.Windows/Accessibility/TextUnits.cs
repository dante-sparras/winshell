namespace Winshell.Platform.Windows.Accessibility;

/// <summary>
/// The units UI Automation moves through text by: characters, words, lines, and the whole document. A word runs to
/// the start of the next one, so it takes its trailing spaces, as in Windows' own edit controls. Lines and
/// paragraphs both end at line breaks. Format is not tracked, so it moves as a word; a page is the document.
/// </summary>
internal static class TextUnits
{
    public const int Character = 0, Format = 1, Word = 2, Line = 3, Paragraph = 4, Page = 5, Document = 6;

    /// <summary>Whether a unit starts at a position.</summary>
    public static bool IsBoundary(string text, int position, int unit)
    {
        if (position <= 0 || position >= text.Length)
            return position == 0 || (position == text.Length && unit == Character);
        return unit switch
        {
            Character => !char.IsLowSurrogate(text[position]),
            Format or Word => !char.IsWhiteSpace(text[position]) && (char.IsWhiteSpace(text[position - 1]) || IsPunctuation(text[position]) != IsPunctuation(text[position - 1])),
            Line or Paragraph => text[position - 1] == '\n',
            _ => false,
        };
    }

    private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

    /// <summary>The start of the unit a position is in.</summary>
    public static int UnitStart(string text, int position, int unit)
    {
        position = Math.Clamp(position, 0, text.Length);
        while (position > 0 && !IsBoundary(text, position, unit))
            position--;
        return position;
    }

    /// <summary>The start of the unit after the one a position is in, or the end of the text.</summary>
    public static int NextStart(string text, int position, int unit)
    {
        position = Math.Clamp(position, 0, text.Length);
        do
            position++;
        while (position < text.Length && !IsBoundary(text, position, unit));
        return Math.Min(position, text.Length);
    }

    /// <summary>The last unit start before a position, or 0.</summary>
    public static int PreviousStart(string text, int position, int unit)
    {
        position = Math.Clamp(position, 0, text.Length);
        if (position == 0)
            return 0;
        do
            position--;
        while (position > 0 && !IsBoundary(text, position, unit));
        return position;
    }

    /// <summary>The unit a range's start is in: what ExpandToEnclosingUnit makes the range.</summary>
    public static (int Start, int End) Enclose(string text, int start, int unit)
    {
        var from = UnitStart(text, start, unit);
        return (from, from >= text.Length ? text.Length : NextStart(text, from, unit));
    }

    /// <summary>
    /// Moves a range by whole units, as ITextRangeProvider::Move does: the range starts at the start of its unit,
    /// moves <paramref name="count"/> units, and covers one unit, or none if it was empty.
    /// </summary>
    public static (int Start, int End, int Moved) Move(string text, int start, int end, int unit, int count)
    {
        var empty = start == end;
        var position = UnitStart(text, start, unit);
        var moved = 0;
        while (moved < count)
        {
            var next = NextStart(text, position, unit);
            // An empty range may move to the very end of the text, a range of one unit may not.
            if (next == position || (next >= text.Length && !empty))
                break;
            position = next;
            moved++;
        }
        while (moved > count && position > 0)
        {
            position = PreviousStart(text, position, unit);
            moved--;
        }
        return (position, empty ? position : NextStart(text, position, unit), moved);
    }

    /// <summary>Moves one endpoint by whole units, as ITextRangeProvider::MoveEndpointByUnit does.</summary>
    public static (int Position, int Moved) MoveEndpoint(string text, int position, int unit, int count)
    {
        var moved = 0;
        while (moved < count && position < text.Length)
        {
            position = NextStart(text, position, unit);
            moved++;
        }
        while (moved > count && position > 0)
        {
            position = PreviousStart(text, position, unit);
            moved--;
        }
        return (position, moved);
    }
}
