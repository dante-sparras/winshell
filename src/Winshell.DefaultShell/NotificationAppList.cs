using Winshell.Notifications;

namespace Winshell.DefaultShell;

/// <summary>An app the notification page can turn off. The name is the id when the app has none.</summary>
public sealed record NotificationApp(string Id, string Name);

/// <summary>
/// The apps on the notifications page. The list only grows while the window is open, so turning an app on or off
/// keeps its card and the keyboard on it.
/// </summary>
public static class NotificationAppList
{
    public static string DoNotDisturbText(NotificationState state) => state switch
    {
        NotificationState.On => "On",
        NotificationState.Off => "Off",
        _ => "Unknown",
    };

    /// <summary>The apps with notifications now, then the ones turned off before that are not among them.</summary>
    public static IReadOnlyList<NotificationApp> Found(
        IEnumerable<(string Id, string Name)> groups,
        IEnumerable<(string Id, string Name)> hidden)
    {
        var seen = new List<NotificationApp>();
        foreach (var (id, name) in groups)
            Add(seen, id, name);
        foreach (var (id, name) in hidden)
            Add(seen, id, name);
        return seen;
    }

    public static IReadOnlyList<NotificationApp> Merge(IReadOnlyList<NotificationApp> current, IReadOnlyList<NotificationApp> found)
    {
        var list = new List<NotificationApp>(current);
        var ids = current.Select(app => app.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var app in found)
        {
            if (ids.Add(app.Id))
                list.Add(app);
        }
        list.Sort(static (a, b) =>
        {
#pragma warning disable CA1309 // App names sort the way the user reads them.
            var byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
#pragma warning restore CA1309
            return byName != 0 ? byName : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });
        return list;
    }

    private static void Add(List<NotificationApp> apps, string id, string name)
    {
        if (id.Length == 0 || apps.Any(app => app.Id == id))
            return;
        apps.Add(new NotificationApp(id, name.Length == 0 ? id : name));
    }
}
