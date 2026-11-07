using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>Virtual desktops a test changes by hand, so no real desktop is switched, added or removed.</summary>
internal sealed class FakeDesktops : IVirtualDesktopBackend
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

    private readonly Listeners<Action> _switched = new();

    public IDisposable ListenSwitched(Action handler, Action<Exception> failed)
    {
        return _switched.Add(handler, failed);
    }

    public bool StartWatching()
    {
        return true;
    }

    /// <summary>Reports a switch the way Windows does, without changing <see cref="Current"/>.</summary>
    public void Switch()
    {
        _switched.Raise(0, static (handler, _) => handler());
    }

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

    public bool SwitchTo(Guid id, out string problem)
    {
        Calls.Add($"switch:{id:D}");
        problem = "";
        if (Items.All(item => item.Id != id))
            return false;

        Current = id;
        return true;
    }

    public Guid? Create(out string problem)
    {
        problem = "";
        var id = Guid.NewGuid();
        Items.Add(new DesktopRecord(id, ""));
        Calls.Add($"create:{id:D}");
        return id;
    }

    public bool Remove(Guid id, Guid fallback, out string problem)
    {
        problem = "";
        Calls.Add($"remove:{id:D}:{fallback:D}");
        Items.RemoveAll(item => item.Id == id);
        if (Current == id)
            Current = fallback;
        return true;
    }
}
