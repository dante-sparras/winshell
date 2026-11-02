using System.Collections.ObjectModel;
using System.Globalization;

namespace Winshell.Notifications;

/// <summary>A view of Windows Notification Center. Making one never prompts for access.</summary>
public sealed class NotificationListener : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly INotificationBackend _backend;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task? _monitorTask;
    private NotificationAccessStatus _accessStatus;
    private NotificationState _focusState;
    private NotificationMode _notificationMode = NotificationMode.Unknown;
    private string _error = "";
    private bool _requesting;
    private bool _disposed;
    private long _revision;

    public NotificationListener(IEventLoop loop)
        : this(loop, Log.Default, new WindowsNotificationBackend(), monitor: true)
    {
    }

    internal NotificationListener(IEventLoop loop, Log log, INotificationBackend backend, bool monitor = false)
    {
        _loop = loop;
        _log = log;
        _backend = backend;
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
        _ => NotificationState.Unknown,
    };

    /// <summary>Why access or the last operation failed.</summary>
    public string Error => _error;

    internal Task? MonitorTask => _monitorTask;

    internal IEventLoop Loop => _loop;

    internal Log Log => _log;

    internal bool IsDisposed => _disposed;

    internal CancellationToken Cancellation => _stop.Token;

    /// <summary>Asks Windows for access and returns at once. Call from a button in a foreground window.</summary>
    public void RequestAccess() => _ = RequestAccessAsync();

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
        catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
        {
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
                    unwatch ??= _backend.WatchToasts(() => changed.Release());
                    await RefreshAsync().ConfigureAwait(false);
                    warning.Succeeded();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                catch (ObjectDisposedException) { return; }
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
                    while (changed.Wait(0)) { }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
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
            var snapshot = await _backend.ReadAsync(_stop.Token).ConfigureAwait(false);
            Post(() =>
            {
                // A read started before dismissal or a permission request must not undo that operation.
                if (revision == Interlocked.Read(ref _revision) && !_requesting)
                    Apply(snapshot);
            });
        }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException) { return; }
        catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
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
        var ordered = snapshot.Notifications.DistinctBy(t => t.Id).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id);
        Notifications.Sync(ordered, n => n.Id, t => t.Id, t =>
        {
            var notification = new Notification(this, t);
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
            NotificationAccessStatus.Denied => "Notification access was denied. Allow access in Windows Settings to use the listener.",
            NotificationAccessStatus.Unavailable => "Notification access is unavailable. A package identity with the userNotificationListener capability may be required.",
            _ => "",
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
        var grouped = Notifications.GroupBy(n => n.AppId.Length > 0 ? n.AppId : "notification:" + n.Id.ToString(CultureInfo.InvariantCulture)).ToArray();
        Groups.Sync(grouped, g => g.Key, g => g.Key, g => new NotificationGroup(this, g.Key), (group, items) => group.Update([.. items]));
    }

    /// <summary>Dismisses this listener's current snapshot. Returns whether every removal succeeded; failed ones stay.</summary>
    public bool ClearAll() => DismissMany(Notifications.ToArray());

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
                Interlocked.Increment(ref _revision);
                Notifications.Remove(notification);
                SyncGroups();
                notification.LetGo();
            }
            catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
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
        Set(ref _error, string.IsNullOrWhiteSpace(message) ? "The Windows notification operation failed." : message, nameof(Error));
        OperationFailed?.Invoke(_error);
    }

    private void Post(Action action) => _loop.Post(() =>
    {
        if (!_disposed)
            action();
    });

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop.Cancel();
        ServiceSupport.DisposeAfterMonitor(_stop, _monitorTask);
        foreach (var notification in Notifications)
            notification.LetGo();
    }
}

/// <summary>
/// A notification in Notification Center. The object stays the same while its content changes. It stays usable after
/// it leaves the center, so a UI can finish an exit transition, but its logo is gone by then.
/// </summary>
public sealed class Notification : ObservableObject
{
    private readonly NotificationListener _listener;
    private IncomingToast _toast;
    private string _appIcon = "";
    private string? _loadedKey;
    private IconHold _hold;
    private int _iconRequest;

    internal Notification(NotificationListener listener, IncomingToast toast)
    {
        _listener = listener;
        _toast = toast;
    }

    /// <summary>The Windows notification id.</summary>
    public uint Id => _toast.Id;

    /// <summary>The AppUserModelID of the app that sent it. Can be empty.</summary>
    public string AppId => _toast.AppId;

    public string AppName => _toast.AppName;

    /// <summary>A <c>file:///</c> url of the sending app's logo. Empty until the read finishes, and when Windows gives none.</summary>
    public string AppIcon => _appIcon;

    public string Title => _toast.Title;

    public string Body => _toast.Body;

    public DateTimeOffset CreatedAt => _toast.CreatedAt;

    /// <summary>When it expires, or null when Windows supplied no expiry.</summary>
    public DateTimeOffset? ExpiresAt => _toast.ExpiresAt;

    /// <summary>Always false: Windows gives a shell no way to activate another app's notification.</summary>
    public bool CanInvoke => false;

    /// <summary>Removes the notification from Windows. Returns true only when Windows accepted the removal.</summary>
    public bool Dismiss() => _listener.DismissMany([this]);

    /// <summary>UserNotificationListener exposes no activation arguments or actions for other apps.</summary>
    public bool Invoke()
    {
        _listener.Fail("Windows UserNotificationListener does not support invoking another app's notification. Use Windows Notification Center to activate it.");
        return false;
    }

    private string IconKey => _toast.AppId.Length > 0
        ? _toast.AppId
        : "id:" + _toast.Id.ToString(CultureInfo.InvariantCulture);

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

    /// <summary>Starts a logo read when this notification's app does not already have one in flight.</summary>
    private void SyncIcon()
    {
        if (_listener.IsDisposed)
            return;
        var source = _toast.Logo;
        var key = source is null ? null : IconKey;
        if (string.Equals(key, _loadedKey, StringComparison.Ordinal))
            return;
        _loadedKey = key;
        LetGo();
        if (source is null || key is null)
            return;
        var request = _iconRequest;
        var label = _toast.AppName.Length > 0 ? _toast.AppName : _toast.AppId.Length > 0 ? _toast.AppId : "a notification";
        var (loop, log, cancellation) = (_listener.Loop, _listener.Log, _listener.Cancellation);
        // Not the thread pool. Publishing the url has to run even when every pool thread is busy.
        var thread = new Thread(() =>
        {
            var read = NotificationAppIcons.Read(key, label, source, log, cancellation);
            // Posted even after the listener is gone, so a finished read can still release its file.
            loop.Post(() => ApplyIcon(read, request));
        })
        {
            IsBackground = true,
            Name = "Notification icon",
        };
        thread.Start();
    }

    private void ApplyIcon(IconRead read, int request)
    {
        // A read that finished after dismissal must not land here, and must not keep the file.
        if (request != _iconRequest)
        {
            NotificationAppIcons.Release(read.Hold);
            return;
        }
        _hold = read.Hold;
        Set(ref _appIcon, read.Url, nameof(AppIcon));
    }

    /// <summary>Drops a finished read, and makes an in-flight one release itself when it lands.</summary>
    internal void LetGo()
    {
        _iconRequest++;
        var hold = _hold;
        _hold = default;
        NotificationAppIcons.Release(hold);
        Set(ref _appIcon, "", nameof(AppIcon));
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

    public string AppId => _appId;

    public string AppName => _appName;

    /// <summary>The app's notifications, newest first.</summary>
    public ObservableCollection<Notification> Notifications { get; } = [];

    /// <summary>Dismisses the group's current notifications. Returns whether every removal succeeded; failed ones stay.</summary>
    public bool Dismiss() => _listener.DismissMany(Notifications.ToArray());

    internal void Update(Notification[] items)
    {
        Set(ref _appId, items[0].AppId, nameof(AppId));
        Set(ref _appName, items[0].AppName, nameof(AppName));
        Notifications.Sync(items, n => n, n => n, n => n, (_, _) => { });
    }
}
