using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Winshell.Apps;

namespace Winshell.Tray;

/// <summary>Windows' choice for one icon: shown in the taskbar, or in the hidden icons flyout.</summary>
internal sealed record TrayIconSetting(Guid Guid, string ExecutablePath, uint? Id, bool Promoted);

/// <summary>
/// The per-icon choice in Settings, under Taskbar, Other system tray icons. Explorer keeps it in
/// <c>HKCU\Control Panel\NotifyIconSettings</c>, one key per icon it has seen: the icon's GUID, or its program and
/// id, and <c>IsPromoted</c> when it is shown in the taskbar. Explorer writes the key when an icon first appears.
/// </summary>
internal static partial class TrayIconSettings
{
    public const string KeyPath = @"Control Panel\NotifyIconSettings";

    public static List<TrayIconSetting> Read()
    {
        using var root = Registry.CurrentUser.OpenSubKey(KeyPath);
        return root is null ? [] : Read(root);
    }

    public static List<TrayIconSetting> Read(RegistryKey root)
    {
        var settings = new List<TrayIconSetting>();
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key is null)
                continue;
            var guid = key.GetValue("IconGuid") is string text && Guid.TryParse(text, out var g) ? g : Guid.Empty;
            var path = key.GetValue("ExecutablePath") is string p ? AppUsageReader.ExpandKnownFolder(p) : "";
            uint? id = key.GetValue("UID") is int uid ? unchecked((uint)uid) : null;
            var promoted = key.GetValue("IsPromoted") is int value && value != 0;
            settings.Add(new TrayIconSetting(guid, path, id, promoted));
        }
        return settings;
    }

    /// <summary>
    /// Whether the icon belongs in the hidden icons flyout. An icon Windows has no choice for yet is hidden, as
    /// Windows 11 does with a new icon.
    /// </summary>
    public static bool InOverflow(IEnumerable<TrayIconSetting> settings, Guid guid, string program, uint id)
    {
        foreach (var s in settings)
        {
            var match = guid != Guid.Empty
                ? s.Guid == guid
                : s.Guid == Guid.Empty && s.Id == id && program.Length > 0 && string.Equals(s.ExecutablePath, program, StringComparison.OrdinalIgnoreCase);
            if (match)
                return !s.Promoted;
        }
        return true;
    }

    /// <summary>Calls back on the loop when Explorer changes any icon's choice, until disposed.</summary>
    public static IDisposable Watch(IEventLoop loop, Action changed) => new Watcher(loop, changed);

    private sealed class Watcher : IDisposable
    {
        private readonly IEventLoop _loop;
        private readonly Action _changed;
        private readonly RegistryKey? _key;
        private readonly AutoResetEvent _event = new(false);
        private readonly RegisteredWaitHandle? _wait;
        private IDisposable? _settle;
        private bool _disposed;

        public Watcher(IEventLoop loop, Action changed)
        {
            _loop = loop;
            _changed = changed;
            _key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (_key is null || !Arm())
                return;
            _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => _loop.Post(OnChanged), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        private bool Arm() =>
            RegNotifyChangeKeyValue(_key!.Handle, true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC, _event.SafeWaitHandle, true) == 0;

        private void OnChanged()
        {
            if (_disposed)
                return;
            Arm();
            // Explorer writes several values for one change; read them once it is done.
            _settle?.Dispose();
            _settle = _loop.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                _settle = null;
                if (!_disposed)
                    _changed();
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _settle?.Dispose();
            _wait?.Unregister(null);
            _key?.Dispose();
            _event.Dispose();
        }
    }

    private const uint REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4, REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint filter, SafeWaitHandle evt, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
