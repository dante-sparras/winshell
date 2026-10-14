using System.Text.Json;

namespace Winshell.Core;

/// <summary>
/// Edits a private copy of a <see cref="BarLayoutDocument"/>. The copy has every section and list, so an edit never meets null.
/// <c>winoma plugin</c> and <c>winoma bar</c> use it, then compare the copy with the document on disk.
/// </summary>
internal sealed class BarLayoutEditor
{
    private readonly BarSections _layout;

    private BarLayoutEditor(BarLayoutDocument document)
    {
        document.Layout ??= new BarSections();
        document.Layout.Left ??= [];
        document.Layout.Center ??= [];
        document.Layout.Right ??= [];
        document.DisabledPlugins ??= [];
        document.Plugins ??= [];
        Document = document;
        _layout = document.Layout;
    }

    /// <summary>The edited copy.</summary>
    public BarLayoutDocument Document { get; }

    /// <summary>An editor over a copy of <paramref name="document"/>, or of the default bar when it is null.</summary>
    /// <param name="document">The bar object from <c>settings.json</c>. It is not changed.</param>
    /// <returns>The editor.</returns>
    public static BarLayoutEditor Copy(BarLayoutDocument? document)
    {
        var json = JsonSerializer.Serialize(document ?? InstalledBar.OrEmpty(), ShellLooseJson.Default.BarLayoutDocument);
        return new BarLayoutEditor(JsonSerializer.Deserialize(json, ShellLooseJson.Default.BarLayoutDocument) ?? new BarLayoutDocument());
    }

    /// <summary>The slots in <paramref name="section"/>. Anything but <c>left</c> and <c>right</c> is the center.</summary>
    /// <param name="section">A section name.</param>
    /// <returns>The live list.</returns>
    public List<BarSlot> Section(string section) => section switch
    {
        "left" => _layout.Left,
        "right" => _layout.Right,
        _ => _layout.Center,
    };

    /// <summary>The section that holds <paramref name="id"/>, searched left, center, right.</summary>
    /// <param name="id">A slot id.</param>
    /// <param name="index">The slot's index in that section, or -1.</param>
    /// <returns>The live list, or null when no section holds the id.</returns>
    public List<BarSlot>? Home(string id, out int index)
    {
        foreach (var slots in new[] { _layout.Left, _layout.Center, _layout.Right })
        {
            index = slots.FindIndex(slot => slot.Id.Equals(id, StringComparison.Ordinal));
            if (index >= 0)
            {
                return slots;
            }
        }

        index = -1;
        return null;
    }

    /// <summary>The largest index <see cref="Move"/> accepts for <paramref name="id"/> in <paramref name="section"/>.</summary>
    /// <param name="id">A slot id.</param>
    /// <param name="section">The destination section.</param>
    /// <returns>The limit, or -1 when the id is not on the bar.</returns>
    public int MoveLimit(string id, string section)
    {
        if (Home(id, out _) is not List<BarSlot> home)
        {
            return -1;
        }

        var destination = Section(section);
        return ReferenceEquals(home, destination) ? destination.Count - 1 : destination.Count;
    }

    /// <summary>
    /// Turns <paramref name="manifest"/> on. A bar widget gets a slot in its default section unless it or the built-in it was cloned from is showing.
    /// A user plugin joins <see cref="BarLayoutDocument.Plugins"/>. Slot values the schema refuses are dropped.
    /// </summary>
    /// <param name="manifest">The plugin.</param>
    /// <returns>A line for each dropped value.</returns>
    public List<string> Enable(PluginManifest manifest)
    {
        RemoveId(Document.DisabledPlugins, manifest.Id);
        var sourceShown = manifest.ClonedFrom.Length > 0
            && !Document.DisabledPlugins.Contains(manifest.ClonedFrom)
            && Home(manifest.ClonedFrom, out _) is not null;
        var widget = manifest.Kinds.Contains(PluginKind.BarWidget);
        if (widget && !sourceShown && Home(manifest.Id, out _) is null)
        {
            var section = manifest.DefaultSection is "left" or "right" ? manifest.DefaultSection : "center";
            Section(section).Add(new BarSlot { Id = manifest.Id });
        }

        if (!manifest.FirstParty && !Document.Plugins.Contains(manifest.Id))
        {
            Document.Plugins.Add(manifest.Id);
        }

        if (Home(manifest.Id, out var index) is not List<BarSlot> home)
        {
            return [];
        }

        return SlotSettings.Clean(home[index], manifest);
    }

    /// <summary>Turns <paramref name="manifest"/> off. A built-in keeps its slot and is listed as disabled. A user plugin leaves the bar.</summary>
    /// <param name="manifest">The plugin.</param>
    public void Disable(PluginManifest manifest)
    {
        if (manifest.FirstParty)
        {
            if (!Document.DisabledPlugins.Contains(manifest.Id))
            {
                Document.DisabledPlugins.Add(manifest.Id);
            }

            return;
        }

        RemoveId(Document.Plugins, manifest.Id);
        RemoveId(Document.DisabledPlugins, manifest.Id);
        _layout.Left.RemoveAll(slot => slot.Id.Equals(manifest.Id, StringComparison.Ordinal));
        _layout.Center.RemoveAll(slot => slot.Id.Equals(manifest.Id, StringComparison.Ordinal));
        _layout.Right.RemoveAll(slot => slot.Id.Equals(manifest.Id, StringComparison.Ordinal));
    }

    /// <summary>Adds a slot for <paramref name="id"/>. Does nothing when the id is already on the bar.</summary>
    /// <param name="id">A slot id.</param>
    /// <param name="section">The section.</param>
    /// <param name="at">The index, or null for the end.</param>
    public void Put(string id, string section, int? at)
    {
        if (Home(id, out _) is not null)
        {
            return;
        }

        var slots = Section(section);
        slots.Insert(at ?? slots.Count, new BarSlot { Id = id });
    }

    /// <summary>Moves the slot for <paramref name="id"/>. Does nothing when the id is not on the bar.</summary>
    /// <param name="id">A slot id.</param>
    /// <param name="section">The destination section.</param>
    /// <param name="at">The index after the slot leaves its section, or null for the end.</param>
    public void Move(string id, string section, int? at)
    {
        var home = Home(id, out var fromIndex);
        if (home is null)
        {
            return;
        }

        var slot = home[fromIndex];
        var destination = Section(section);
        home.RemoveAt(fromIndex);
        destination.Insert(at ?? destination.Count, slot);
    }

    /// <summary>
    /// Puts the slot for <paramref name="id"/> beside a neighbour in <paramref name="section"/>, as a drag on the bar leaves it.
    /// An id with no slot gets one. With neither neighbour in that section, the slot goes to its end.
    /// </summary>
    /// <param name="id">A slot id.</param>
    /// <param name="section">The destination section.</param>
    /// <param name="before">The slot it lands in front of, or null.</param>
    /// <param name="after">The slot it lands behind when <paramref name="before"/> is not in the section, or null.</param>
    public void Drop(string id, string section, string? before, string? after)
    {
        var slot = new BarSlot { Id = id };
        if (Home(id, out var from) is List<BarSlot> home)
        {
            slot = home[from];
            home.RemoveAt(from);
        }

        var destination = Section(section);
        var at = Index(destination, before);
        if (at < 0)
        {
            at = Index(destination, after) is var behind and >= 0 ? behind + 1 : destination.Count;
        }

        destination.Insert(at, slot);
    }

    /// <summary>Stores <paramref name="stored"/> under <paramref name="key"/> on the slot for <paramref name="id"/>.</summary>
    /// <param name="id">A slot id.</param>
    /// <param name="key">The setting name.</param>
    /// <param name="stored">The value <see cref="SlotSettings.TryStore"/> produced.</param>
    public void Set(string id, string key, string stored)
    {
        var home = Home(id, out var index);
        if (home is null)
        {
            return;
        }

        var slot = home[index];
        if (slot.Settings.TryGetValue(key, out var current) && string.Equals(current, stored, StringComparison.Ordinal))
        {
            return;
        }

        slot.Settings[key] = stored;
    }

    private static int Index(List<BarSlot> slots, string? id) =>
        id is null ? -1 : slots.FindIndex(slot => slot.Id.Equals(id, StringComparison.Ordinal));

    private static void RemoveId(List<string> ids, string id) =>
        ids.RemoveAll(item => item.Equals(id, StringComparison.Ordinal));
}
