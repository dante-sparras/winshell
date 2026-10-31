using System.Globalization;
using System.Text;

namespace Winshell.Platform.Windows.Accessibility;

/// <summary>
/// The units UI Automation moves through text by: characters, words, lines, and the whole document. A character is
/// one text element, so a combining mark or an emoji sequence stays together. A word runs to the start of the next
/// one, so it takes its trailing spaces, as in Windows' own edit controls. Each ideograph is its own word. There is
/// no dictionary, so a run of Thai, kana, or Hangul stays one word. Lines and paragraphs both end at line breaks.
/// Format is not tracked, so it moves as a word; a page is the document.
/// </summary>
internal static class TextUnits
{
    public const int Character = 0, Format = 1, Word = 2, Line = 3, Paragraph = 4, Page = 5, Document = 6;

    /// <summary>Whether a unit starts at a position.</summary>
    public static bool IsBoundary(string text, int position, int unit) =>
        IsBoundary(text, position, unit, TextElements(text, unit));

    private static bool IsBoundary(string text, int position, int unit, int[]? elements)
    {
        if (position <= 0 || position >= text.Length)
            return position == 0 || (position == text.Length && unit == Character);
        return unit switch
        {
            Character => elements is not null && Array.BinarySearch(elements, position) >= 0,
            Format or Word => IsWordStart(text, position, elements),
            Line or Paragraph => text[position - 1] == '\n',
            _ => false,
        };
    }

    /// <summary>Text-element starts when the unit follows them, otherwise null.</summary>
    private static int[]? TextElements(string text, int unit) =>
        unit is Character or Format or Word ? StringInfo.ParseCombiningCharacters(text) : null;

    private static bool IsWordStart(string text, int position, int[]? elements)
    {
        if (elements is null)
            return false;
        var index = Array.BinarySearch(elements, position);
        if (index <= 0)
            return false;
        if (char.IsWhiteSpace(text[position]))
            return false;
        var previous = elements[index - 1];
        if (IsIdeograph(text, position) || IsIdeograph(text, previous))
            return true;
        return char.IsWhiteSpace(text[previous]) || IsPunctuation(text[position]) != IsPunctuation(text[previous]);
    }

    private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

    /// <summary>Whether the scalar at a text-element start is Unicode Ideographic. This is not a word list.</summary>
    private static bool IsIdeograph(string text, int index) =>
        Rune.TryGetRuneAt(text, index, out var rune) && rune.Value is 0x3006 or 0x3007
            or (>= 0x3021 and <= 0x3029)
            or (>= 0x3038 and <= 0x303A)
            or (>= 0x3400 and <= 0x4DBF)
            or (>= 0x4E00 and <= 0x9FFF)
            or (>= 0xF900 and <= 0xFAFF)
            or (>= 0x20000 and <= 0x2A6DF)
            or (>= 0x2A700 and <= 0x2EBEF)
            or (>= 0x2F800 and <= 0x2FA1F)
            or (>= 0x30000 and <= 0x323AF);

    /// <summary>The start of the unit a position is in.</summary>
    public static int UnitStart(string text, int position, int unit)
    {
        position = Math.Clamp(position, 0, text.Length);
        var elements = TextElements(text, unit);
        while (position > 0 && !IsBoundary(text, position, unit, elements))
            position--;
        return position;
    }

    /// <summary>The start of the unit after the one a position is in, or the end of the text.</summary>
    public static int NextStart(string text, int position, int unit)
    {
        position = Math.Clamp(position, 0, text.Length);
        var elements = TextElements(text, unit);
        do
            position++;
        while (position < text.Length && !IsBoundary(text, position, unit, elements));
        return Math.Min(position, text.Length);
    }

    /// <summary>The last unit start before a position, or 0.</summary>
    public static int PreviousStart(string text, int position, int unit)
    {
        position = Math.Clamp(position, 0, text.Length);
        if (position == 0)
            return 0;
        var elements = TextElements(text, unit);
        do
            position--;
        while (position > 0 && !IsBoundary(text, position, unit, elements));
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
