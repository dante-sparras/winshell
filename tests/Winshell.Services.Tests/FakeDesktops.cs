using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>Virtual desktops a test changes by hand, so no real desktop is switched, added or removed.</summary>
internal sealed class FakeDesktops : IVirtualDesktopSource
{
    public static FakeDesktops Holding(Guid current, params Guid[] ids)
    {
        var source = new FakeDesktops { Current = current };
        foreach (var id in ids)
            source.Items.Add(new DesktopRecord(id, ""));
        return source;
    }

    public DesktopContract Contract { get; set; } = DesktopContract.ForBuild(26300);

    public List<DesktopRecord> Items { get; } = [];

    public Guid Current { get; set; }

    public List<string> Calls { get; } = [];

    public bool FailRead { get; set; }

    public string ListStamp => string.Join(",", Items.Select(item => item.Id));

    public bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error)
    {
        Calls.Add("read");
        if (FailRead)
        {
            desktops = [];
            current = Guid.Empty;
            error = "failed: boom";
            return false;
        }
        desktops = Items.ToArray();
        current = Current;
        error = "";
        return true;
    }

    public bool SwitchTo(Guid id)
    {
        Calls.Add($"switch:{id:D}");
        if (Items.All(item => item.Id != id))
            return false;
        Current = id;
        return true;
    }

    public Guid? Create()
    {
        var id = Guid.NewGuid();
        Items.Add(new DesktopRecord(id, ""));
        Calls.Add($"create:{id:D}");
        return id;
    }

    public bool Remove(Guid id, Guid fallback)
    {
        Calls.Add($"remove:{id:D}:{fallback:D}");
        Items.RemoveAll(item => item.Id == id);
        if (Current == id)
            Current = fallback;
        return true;
    }
}
