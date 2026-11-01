namespace Winshell.Apps;

/// <summary>
/// What <see cref="IJumpListReader"/> returned for one app. <see cref="Available"/> is false when the lists cannot
/// be read. An empty <see cref="Names"/> with <see cref="Available"/> true was read and has no documents.
/// </summary>
internal readonly record struct JumpListRead(bool Available, IReadOnlyList<string> Names)
{
    /// <summary>How many names one category contributes. The menu shows at most twice this, recent first.</summary>
    public const int PerCategory = 10;

    public static JumpListRead Missing => new(false, []);

    /// <summary>
    /// Recent names first, then frequent names not already listed. A null category was not read. When both are
    /// null, the list cannot be read. A name that is only whitespace is left out.
    /// </summary>
    public static JumpListRead From(IReadOnlyList<string>? recent, IReadOnlyList<string>? frequent)
    {
        if (recent is null && frequent is null)
            return Missing;
        var names = new List<string>();
        Add(recent);
        Add(frequent);
        return new(true, names);

        void Add(IReadOnlyList<string>? source)
        {
            if (source is null)
                return;
            var taken = 0;
            foreach (var name in source)
            {
                if (taken >= PerCategory)
                    return;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var shown = name.Trim();
                if (names.Exists(have => have.Equals(shown, StringComparison.OrdinalIgnoreCase)))
                    continue;
                names.Add(shown);
                taken++;
            }
        }
    }
}

/// <summary>Reads recent and frequent destinations for an AppUserModelID. It does not launch or change them.</summary>
internal interface IJumpListReader
{
    JumpListRead Read(string appUserModelId);
}
