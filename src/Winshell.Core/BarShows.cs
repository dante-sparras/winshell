using System.Text.Json.Serialization;

namespace Winshell.Core;

/// <summary>How much of a meter the bar draws.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BarShows>))]
public enum BarShows
{
    /// <summary>Just the mark.</summary>
    Icon,

    /// <summary>The mark and the percent.</summary>
    Percent,

    /// <summary>The mark, a meter, and the percent.</summary>
    Full,
}
