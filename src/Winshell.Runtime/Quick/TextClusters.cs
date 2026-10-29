using System.Globalization;

namespace Winshell.Runtime.Quick;

/// <summary>Grapheme-cluster boundaries. A caret or a cut never stops inside a surrogate pair.</summary>
internal static class TextClusters
{
    /// <summary>The boundary before <paramref name="index"/>, or the start of the cluster that contains it.</summary>
    public static int Previous(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == 0)
            return 0;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var i = Array.BinarySearch(starts, index);
        if (i >= 0)
            return i == 0 ? 0 : starts[i - 1];
        i = ~i;
        return starts[i - 1];
    }

    /// <summary>The boundary after <paramref name="index"/>.</summary>
    public static int Next(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index >= text.Length)
            return text.Length;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var i = Array.BinarySearch(starts, index);
        if (i >= 0)
            return i + 1 < starts.Length ? starts[i + 1] : text.Length;
        i = ~i;
        return i < starts.Length ? starts[i] : text.Length;
    }

    /// <summary>The cluster boundary at <paramref name="index"/>, or the start of the cluster that contains it.</summary>
    public static int Snap(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == 0 || index == text.Length)
            return index;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var i = Array.BinarySearch(starts, index);
        if (i >= 0)
            return index;
        i = ~i;
        return starts[i - 1];
    }

    /// <summary>The cluster boundary at <paramref name="index"/>, or the next one when <paramref name="index"/> is inside a cluster.</summary>
    public static int Ceil(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == text.Length)
            return text.Length;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var i = Array.BinarySearch(starts, index);
        if (i >= 0)
            return index;
        i = ~i;
        return i < starts.Length ? starts[i] : text.Length;
    }

    /// <summary>A prefix of at most <paramref name="maximumUnits"/> UTF-16 units that ends on a cluster boundary.</summary>
    public static string Limit(string text, int maximumUnits)
    {
        if (string.IsNullOrEmpty(text) || maximumUnits <= 0)
            return "";
        if (text.Length <= maximumUnits)
            return text;
        return text[..Snap(text, maximumUnits)];
    }

    public static int Count(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>How many clusters begin before <paramref name="index"/>.</summary>
    public static int CountBefore(string text, int index)
    {
        index = Snap(text, index);
        if (index == 0)
            return 0;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var i = Array.BinarySearch(starts, index);
        return i >= 0 ? i : ~i;
    }

    /// <summary>The UTF-16 index where cluster number <paramref name="element"/> starts.</summary>
    public static int IndexOfElement(string text, int element)
    {
        if (element <= 0 || text.Length == 0)
            return 0;
        var starts = StringInfo.ParseCombiningCharacters(text);
        if (element >= starts.Length)
            return text.Length;
        return starts[element];
    }

    public static string First(string text) =>
        string.IsNullOrEmpty(text) ? "" : StringInfo.GetNextTextElement(text);

    public static string Repeat(string element, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(element))
            return "";
        if (element.Length == 1)
            return new string(element[0], count);
        return string.Concat(Enumerable.Repeat(element, count));
    }
}
