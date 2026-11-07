using System.Collections.ObjectModel;
using System.Globalization;

namespace Winshell.Notifications;

/// <summary>A view of Windows Notification Center. Making one never prompts for access.</summary>
public sealed class NotificationListener : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly INotificationBackend _backend;
    private readonly ToastChanges _changes;
    private readonly NotificationAppIcons _icons;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task? _monitorTask;

    /// <summary>
    /// The toasts of the last read. A poll sees the same toasts, each of which costs Windows milliseconds to name its
    /// app, and an app that changes a toast replaces it, which gives it a new creation time.
    /// </summary>
    private Dictionary<(uint Id, DateTimeOffset Created), IncomingToast> _read = [];

    private IReadOnlyList<IncomingToast> _toasts = [];

    private NotificationAccessStatus _accessStatus;
    private NotificationState _focusState;
    private NotificationMode _notificationMode = NotificationMode.Unknown;
    private string _error = "";
    private bool _requesting;
    private bool _disposed;
    private long _revision;

    internal NotificationListener(IEventLoop loop, Log log)
        : this(loop, log, new WindowsNotificationBackend(), true)
    {
    }

    internal NotificationListener(IEventLoop loop, Log log, INotificationBackend backend, bool monitor = false)
    {
        _loop = loop;
        _log = log;
        _backend = backend;
        _changes = new ToastChanges(backend.ToastStamp, loop.Time);
        _icons = new NotificationAppIcons(loop, log, _stop.Token);
        if (monitor)
        {
            var cancellation = _stop.Token;
            _monitorTask = Task.Run(() => MonitorAsync(cancellation));
        }
    }

    /// <summary>Raised after <see cref="Notifications"/> holds a newly seen notification, including those in the first read.</summary>
    public event Action<Notification>? NotificationAdded;

    /// <summary>An access request, a dismissal or <see cref="Notification.Invoke"/> failed. The text is <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    /// <summary>Every readable notification, newest first. Empty without access.</summary>
    public ObservableCollection<Notification> Notifications { get; } = [];

    /// <summary>The notifications in one group per app, newest first.</summary>
    public ObservableCollection<NotificationGroup> Groups { get; } = [];

    /// <summary>Whether the shell may read the notifications. Ask with <see cref="RequestAccess"/>.</summary>
    public NotificationAccessStatus AccessStatus => _accessStatus;

    /// <summary>Whether a Focus session is on.</summary>
    public NotificationState FocusState => _focusState;

    /// <summary>Windows' current notification restriction.</summary>
    public NotificationMode NotificationMode => _notificationMode;

    /// <summary>On for priority-only or alarms-only, Off for unrestricted.</summary>
    public NotificationState DoNotDisturbState => _notificationMode switch
    {
        NotificationMode.Unrestricted => NotificationState.Off,
        NotificationMode.PriorityOnly or NotificationMode.AlarmsOnly => NotificationState.On,
        _ => NotificationState.Unknown
    };

    /// <summary>Why access or the last operation failed.</summary>
    public string Error => _error;

    /// <summary>Asks Windows for access and returns at once. Call from a button in a foreground window.</summary>
    public void RequestAccess()
    {
        _ = RequestAccessAsync();
    }

    internal async Task RequestAccessAsync()
    {
        if (_requesting || _disposed)
            return;
        _requesting = true;
        Interlocked.Increment(ref _revision);
        Set(ref _accessStatus, NotificationAccessStatus.Requesting, nameof(AccessStatus));
        Set(ref _error, "", nameof(Error));
        try
        {
            // Windows requires the UI thread for the permission prompt; do not use Task.Run here.
            var status = await _backend.RequestAccessAsync().ConfigureAwait(false);
            Post(() =>
            {
                _requesting = false;
                Interlocked.Increment(ref _revision);
                SetAccess(status);
            });
        }
#pragma warning disable CA1031 // Nobody awaits this; a failure left unhandled would keep _requesting set and stop every read.
        catch (Exception e)
#pragma warning restore CA1031
        {
            if (!WindowsFailure.IsExpected(e))
                _log.Warn("notifications", $"Requesting access failed: {e}");
            Post(() =>
            {
                _requesting = false;
                Interlocked.Increment(ref _revision);
                SetAccess(NotificationAccessStatus.Unavailable);
                Fail(e.Message);
            });
        }
    }

    private async Task MonitorAsync(CancellationToken cancellation)
    {
        var warning = new MonitorWarning(() => _log, "notifications");
        // Not disposed: a toast event can still arrive after the watch stops.
        var changed = new SemaphoreSlim(0);
        Action? unwatch = null;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                // A failed read tries again sooner than the idle poll.
                var wait = TimeSpan.FromSeconds(10);
                try
                {
                    // With package identity Windows reports toast changes; the poll catches Focus, Do Not Disturb and access.
                    unwatch ??= _backend.WatchToasts(() =>
                    {
                        _changes.Change();
                        changed.Release();
                    });
                    await RefreshAsync().ConfigureAwait(false);
                    warning.Succeeded();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
#pragma warning disable CA1031 // One failed read must not stop the rest of the poll.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    warning.Failed(e);
                    wait = TimeSpan.FromSeconds(2);
                }

                try
                {
                    await changed.WaitAsync(wait, cancellation).ConfigureAwait(false);
                    while (changed.Wait(0, cancellation))
                    {
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        finally
        {
            unwatch?.Invoke();
        }
    }

    internal async Task RefreshAsync()
    {
        var revision = Interlocked.Read(ref _revision);
        try
        {
            var snapshot = await ReadAsync(_stop.Token).ConfigureAwait(false);
            Post(() =>
            {
                // A read started before dismissal or a permission request must not undo that operation.
                if (revision == Interlocked.Read(ref _revision) && !_requesting)
                    Apply(snapshot);
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception e) when (WindowsFailure.IsExpected(e))
        {
            Post(() =>
            {
                if (revision != Interlocked.Read(ref _revision) || _requesting)
                    return;
                SetAccess(NotificationAccessStatus.Unavailable);
                Fail(e.Message);
            });
        }
    }

    private async Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Focus and notification restriction mode do not require listener permission.
        var focus = _backend.ReadFocus();
        var mode = _backend.ReadMode();
        try
        {
            var access = _backend.ReadAccess();
            if (access != NotificationAccessStatus.Allowed)
            {
                _changes.Change();
                return new NotificationSnapshot(access, [], focus, mode);
            }

            if (!_changes.Due(out var stamp))
                return new NotificationSnapshot(access, _toasts, focus, mode);
            var listed = await _backend.ListToastsAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // Access may be revoked while the asynchronous read is in progress.
            access = _backend.ReadAccess();
            if (access != NotificationAccessStatus.Allowed)
            {
                _changes.Change();
                return new NotificationSnapshot(access, [], focus, mode);
            }

            var known = _read;
            Dictionary<(uint, DateTimeOffset), IncomingToast> read = [];
            List<IncomingToast> toasts = [];
            foreach (var listedToast in listed)
                try
                {
                    var key = (listedToast.Id, listedToast.CreatedAt);
                    if (!known.TryGetValue(key, out var toast))
                        toast = listedToast.Read();
                    read[key] = toast;
                    toasts.Add(toast);
                }
                catch (Exception e) when (WindowsFailure.IsExpected(e))
                {
                    // An app can disappear between enumeration and reading its toast.
                    _log.Warn("notifications", e.Message);
                }

            _read = read;
            _toasts = toasts;
            _changes.Read(stamp);
            return new NotificationSnapshot(access, toasts, focus, mode);
        }
        catch (Exception e) when (WindowsFailure.IsExpected(e))
        {
            return new NotificationSnapshot(NotificationAccessStatus.Unavailable, [], focus, mode,
                $"Windows notification access is unavailable: {e.Message} Check package identity and the userNotificationListener capability.");
        }
    }

    internal void Apply(NotificationSnapshot snapshot)
    {
        if (_disposed)
            return;
        SetAccess(snapshot.AccessStatus);
        Set(ref _focusState, snapshot.Focus, nameof(FocusState));
        var doNotDisturb = DoNotDisturbState;
        Set(ref _notificationMode, snapshot.Mode, nameof(NotificationMode));
        Changed(doNotDisturb != DoNotDisturbState, nameof(DoNotDisturbState));
        if (snapshot.Error.Length > 0)
            Set(ref _error, snapshot.Error, nameof(Error));
        if (snapshot.AccessStatus != NotificationAccessStatus.Allowed)
            return;

        List<Notification> added = [];
        var ordered = snapshot.Notifications.DistinctBy(t => t.Id).OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id);
        Notifications.Sync(ordered, n => n.Id, t => t.Id, t =>
        {
            var notification = new Notification(this, _icons, t);
            added.Add(notification);
            return notification;
        }, (n, t) => n.Update(t), n => n.LetGo());
        SyncGroups();
        foreach (var notification in added)
            NotificationAdded?.Invoke(notification);
    }

    private void SetAccess(NotificationAccessStatus status)
    {
        Set(ref _accessStatus, status, nameof(AccessStatus));
        Set(ref _error, status switch
        {
            NotificationAccessStatus.Denied =>
                "Notification access was denied. Allow access in Windows Settings to use the listener.",
            NotificationAccessStatus.Unavailable =>
                "Notification access is unavailable. A package identity with the userNotificationListener capability may be required.",
            _ => ""
        }, nameof(Error));
        if (status != NotificationAccessStatus.Allowed && status != NotificationAccessStatus.Requesting)
        {
            var removed = Notifications.ToArray();
            Notifications.Clear();
            SyncGroups();
            foreach (var notification in removed)
                notification.LetGo();
        }
    }

    private void SyncGroups()
    {
        var grouped = Notifications.GroupBy(n => n.AppKey).ToArray();
        Groups.Sync(grouped, g => g.Key, g => g.Key, g => new NotificationGroup(this, g.Key),
            (group, items) => group.Update([.. items]));
    }

    /// <summary>Dismisses this listener's current snapshot. Returns whether every removal succeeded; failed ones stay.</summary>
    public bool ClearAll()
    {
        return DismissMany(Notifications.ToArray());
    }

    internal bool DismissMany(IEnumerable<Notification> notifications)
    {
        if (_disposed)
            return false;
        var success = true;
        string? failure = null;
        foreach (var notification in notifications.ToArray())
        {
            if (!Notifications.Contains(notification))
            {
                success = false;
                continue;
            }

            try
            {
                _backend.Dismiss(notification.Id);
                _changes.Change();
                Interlocked.Increment(ref _revision);
                Notifications.Remove(notification);
                SyncGroups();
                notification.LetGo();
            }
            catch (Exception e) when (WindowsFailure.IsExpected(e))
            {
                success = false;
                failure = e.Message;
            }
        }

        if (failure is not null)
            Fail(failure);
        else if (success)
            Set(ref _error, "", nameof(Error));
        return success;
    }

    internal void Fail(string message)
    {
        Set(ref _error, string.IsNullOrWhiteSpace(message) ? "The Windows notification operation failed." : message,
            nameof(Error));
        OperationFailed?.Invoke(_error);
    }

    private void Post(Action action)
    {
        _loop.Post(() =>
        {
            if (!_disposed)
                action();
        });
    }

    /// <summary>Stops watching Notification Center and releases the app logos it read.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop.Cancel();
        DisposeAfterMonitor(_stop, _monitorTask);
        foreach (var notification in Notifications)
            notification.LetGo();
    }

    /// <summary>Releases a cancelled monitor's token source only after its task has ended, without waiting.</summary>
    private static void DisposeAfterMonitor(CancellationTokenSource stop, Task? monitor)
    {
        if (monitor is null || monitor.IsCompleted)
            stop.Dispose();
        else
            _ = monitor.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), stop,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

/// <summary>
/// A notification in Notification Center. The object stays the same while its content changes. It stays usable after
/// it leaves the center, so a UI can finish an exit transition, but its logo is gone by then.
/// </summary>
public sealed class Notification : ObservableObject
{
    private readonly NotificationListener _listener;
    private readonly NotificationAppIcons _icons;
    private IncomingToast _toast;
    private string _appIcon = "";
    private string? _loadedKey;
    private int _iconRequest;

    internal Notification(NotificationListener listener, NotificationAppIcons icons, IncomingToast toast)
    {
        _listener = listener;
        _icons = icons;
        _toast = toast;
    }

    /// <summary>The Windows notification id.</summary>
    public uint Id => _toast.Id;

    /// <summary>The AppUserModelID of the app that sent it. Can be empty.</summary>
    public string AppId => _toast.AppId;

    /// <summary>The display name of the app that sent it. Can be empty.</summary>
    public string AppName => _toast.AppName;

    /// <summary>A <c>file:///</c> url of the sending app's logo. Empty until the read finishes, and when Windows gives none.</summary>
    public string AppIcon => _appIcon;

    /// <summary>The first line of text. Can be empty.</summary>
    public string Title => _toast.Title;

    /// <summary>The lines of text after the title, joined by line breaks. Can be empty.</summary>
    public string Body => _toast.Body;

    /// <summary>When the notification was created.</summary>
    public DateTimeOffset CreatedAt => _toast.CreatedAt;

    /// <summary>When it expires, or null when Windows supplied no expiry.</summary>
    public DateTimeOffset? ExpiresAt => _toast.ExpiresAt;

    /// <summary>Always false: Windows gives a shell no way to activate another app's notification.</summary>
    public bool CanInvoke => false;

    /// <summary>Removes the notification from Windows. Returns true only when Windows accepted the removal.</summary>
    public bool Dismiss()
    {
        return _listener.DismissMany([this]);
    }

    /// <summary>UserNotificationListener exposes no activation arguments or actions for other apps.</summary>
    public bool Invoke()
    {
        _listener.Fail(
            "Windows UserNotificationListener does not support invoking another app's notification. Use Windows Notification Center to activate it.");
        return false;
    }

    /// <summary>The sending app, or this notification alone when it has no app identity.</summary>
    internal string AppKey => AppId.Length > 0 ? AppId : "notification:" + Id.ToString(CultureInfo.InvariantCulture);

    internal void Update(IncomingToast toast)
    {
        var old = _toast;
        _toast = toast;
        Changed(old.AppId != toast.AppId, nameof(AppId));
        Changed(old.AppName != toast.AppName, nameof(AppName));
        Changed(old.Title != toast.Title, nameof(Title));
        Changed(old.Body != toast.Body, nameof(Body));
        Changed(old.CreatedAt != toast.CreatedAt, nameof(CreatedAt));
        Changed(old.ExpiresAt != toast.ExpiresAt, nameof(ExpiresAt));
        SyncIcon();
    }

    /// <summary>Starts a logo read when this notification's logo source or app changes.</summary>
    private void SyncIcon()
    {
        var source = _toast.Logo;
        var key = source is null ? null : AppKey;
        if (string.Equals(key, _loadedKey, StringComparison.Ordinal))
            return;
        _loadedKey = key;
        LetGo();
        if (source is null || key is null)
            return;
        var request = _iconRequest;
        var label = _toast.AppName.Length > 0 ? _toast.AppName :
            _toast.AppId.Length > 0 ? _toast.AppId : "a notification";
        _icons.Read(key, label, source, url => ApplyIcon(url, request));
    }

    private void ApplyIcon(string url, int request)
    {
        // A read that finished after dismissal must not land here, and must not keep the file.
        if (request != _iconRequest)
        {
            TempImageStore.Shared.ReleaseLater(url);
            return;
        }

        Set(ref _appIcon, url, nameof(AppIcon));
    }

    /// <summary>Drops a finished read, and makes an in-flight one release itself when it lands.</summary>
    internal void LetGo()
    {
        _iconRequest++;
        var url = _appIcon;
        Set(ref _appIcon, "", nameof(AppIcon));
        TempImageStore.Shared.ReleaseLater(url);
    }
}

/// <summary>The notifications of one app. Notifications without an app identity each get a group of their own.</summary>
public sealed class NotificationGroup : ObservableObject
{
    private readonly NotificationListener _listener;
    private string _appId = "";
    private string _appName = "";

    internal NotificationGroup(NotificationListener listener, string key)
    {
        _listener = listener;
        Key = key;
    }

    internal string Key { get; }

    /// <summary>The AppUserModelID of the app that sent the notifications. Empty when the app has none.</summary>
    public string AppId => _appId;

    /// <summary>The display name of the app that sent the notifications. Can be empty.</summary>
    public string AppName => _appName;

    /// <summary>The app's notifications, newest first.</summary>
    public ObservableCollection<Notification> Notifications { get; } = [];

    /// <summary>Dismisses the group's current notifications. Returns whether every removal succeeded; failed ones stay.</summary>
    public bool Dismiss()
    {
        return _listener.DismissMany(Notifications.ToArray());
    }

    internal void Update(Notification[] items)
    {
        Set(ref _appId, items[0].AppId, nameof(AppId));
        Set(ref _appName, items[0].AppName, nameof(AppName));
        Notifications.Sync(items, n => n, n => n, n => n, (_, _) => { });
    }
}
