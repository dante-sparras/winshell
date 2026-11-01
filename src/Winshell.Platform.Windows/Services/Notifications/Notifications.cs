using NotificationModeValue = Winshell.Notifications.NotificationMode;
using Winshell.Platform.Windows.Services;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Notifications;

/// <summary>A view of Windows Notification Center. Loading it never prompts for access.</summary>
[QmlSignal("notificationAdded", "notification")]
[QmlSignal("operationFailed", "message")]
public sealed class NotificationListener : QObject
{
    private readonly INotificationBackend _backend;
    private readonly bool _monitor;
    private readonly CancellationTokenSource _stop = new();
    private Task? _monitorTask;
    private readonly Dictionary<uint, ShellNotification> _items = [];
    private readonly Dictionary<string, NotificationGroup> _groups = [];
    private NotificationAccessStatus _accessStatus;
    private NotificationState _focusState;
    private NotificationMode _notificationMode = NotificationModeValue.Unknown;
    private string _error = "";
    private bool _requesting;
    private bool _started;
    private long _revision;

    public NotificationListener() : this(new WindowsNotificationBackend(), monitor: true) { }
    internal NotificationListener(INotificationBackend backend, bool monitor = false)
    {
        _backend = backend;
        _monitor = monitor;
        Notifications = new ObjectModel();
        Notifications.SetOwner(this);
        Groups = new ObjectModel();
        Groups.SetOwner(this);
    }

    [Qml] public ObjectModel Notifications { get; }
    [Qml] public ObjectModel Groups { get; }
    [Qml] public int AccessStatus { get { Track(); return (int)_accessStatus; } }
    [Qml] public int FocusState { get { Track(); return (int)_focusState; } }
    [Qml] public int NotificationMode { get { Track(); return (int)_notificationMode; } }
    [Qml] public int DoNotDisturbState
    {
        get
        {
            Track();
            return (int)(_notificationMode switch
            {
                NotificationModeValue.Unrestricted => NotificationState.Off,
                NotificationModeValue.PriorityOnly or
                    NotificationModeValue.AlarmsOnly => NotificationState.On,
                _ => NotificationState.Unknown,
            });
        }
    }
    [Qml] public string Error { get { Track(); return _error; } }
    internal Task? MonitorTask => _monitorTask;
    internal CancellationToken Cancellation => _stop.Token;

    public override void ComponentComplete()
    {
        Notifications.Engine = Groups.Engine = Engine;
        if (_monitor && !_started)
        {
            _started = true;
            var cancellation = _stop.Token;
            _monitorTask = Task.Run(() => MonitorAsync(cancellation));
        }
    }

    /// <summary>Call from a foreground QML button. The native request starts on the caller's UI thread.</summary>
    [Qml] public void RequestAccess() => _ = RequestAccessAsync();

    internal async Task RequestAccessAsync()
    {
        if (_requesting || IsDestroyed)
            return;
        _requesting = true;
        Interlocked.Increment(ref _revision);
        SetField(ref _accessStatus, NotificationAccessStatus.Requesting, nameof(AccessStatus));
        SetField(ref _error, "", nameof(Error));
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
        var warning = new MonitorWarning(() => Engine?.Log ?? Log.Default, "notifications");
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
        if (IsDestroyed)
            return;
        SetAccess(snapshot.AccessStatus);
        SetField(ref _focusState, snapshot.Focus, nameof(FocusState));
        if (SetField(ref _notificationMode, snapshot.Mode, nameof(NotificationMode)))
            NotifyChanged("doNotDisturbState");
        if (snapshot.Error.Length > 0)
            SetField(ref _error, snapshot.Error, nameof(Error));
        if (snapshot.AccessStatus != NotificationAccessStatus.Allowed)
            return;

        var incoming = snapshot.Notifications.DistinctBy(t => t.Id).ToDictionary(t => t.Id);
        var removed = _items.Values.Where(n => !incoming.ContainsKey(n.NativeId)).ToArray();
        foreach (var item in removed)
            _items.Remove(item.NativeId);
        List<ShellNotification> added = [];
        foreach (var toast in incoming.Values)
        {
            if (_items.TryGetValue(toast.Id, out var item))
                item.Update(toast);
            else
            {
                item = new ShellNotification(this, toast) { Engine = Engine };
                item.SetOwner(this);
                _items.Add(toast.Id, item);
                item.SyncIcon();
                added.Add(item);
            }
        }
        RebuildModels();
        foreach (var item in removed)
            RetainableHook.Release(item);
        foreach (var item in added)
            Emit("notificationAdded", item);
    }

    private void SetAccess(NotificationAccessStatus status)
    {
        SetField(ref _accessStatus, status, nameof(AccessStatus));
        SetField(ref _error, status switch
        {
            NotificationAccessStatus.Denied => "Notification access was denied. Allow access in Windows Settings to use the listener.",
            NotificationAccessStatus.Unavailable => "Notification access is unavailable. A package identity with the userNotificationListener capability may be required.",
            _ => "",
        }, nameof(Error));
        if (status != NotificationAccessStatus.Allowed && status != NotificationAccessStatus.Requesting)
        {
            var removed = _items.Values.ToArray();
            _items.Clear();
            RebuildModels();
            foreach (var item in removed)
                RetainableHook.Release(item);
        }
    }

    private void RebuildModels()
    {
        var ordered = _items.Values.OrderByDescending(n => n.CreatedAtValue).ThenByDescending(n => n.NativeId).ToArray();
        Notifications.Set(ordered);
        var grouped = ordered.GroupBy(n => n.AppId.Length > 0 ? n.AppId : $"notification:{n.NativeId}").ToArray();
        var live = grouped.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var removed = _groups.Where(g => !live.Contains(g.Key)).ToArray();
        foreach (var pair in removed)
            _groups.Remove(pair.Key);
        List<NotificationGroup> groups = [];
        foreach (var group in grouped)
        {
            if (!_groups.TryGetValue(group.Key, out var model))
            {
                model = new NotificationGroup(this) { Engine = Engine };
                model.SetOwner(this);
                model.Notifications.Engine = Engine;
                _groups.Add(group.Key, model);
            }
            model.Update(group.ToArray());
            groups.Add(model);
        }
        Groups.Set(groups);
        foreach (var pair in removed)
            pair.Value.Destroy();
    }

    internal bool Dismiss(ShellNotification notification)
        => DismissMany([notification]);

    /// <summary>Dismiss only this listener's current snapshot, preserving failures and newly arriving toasts.</summary>
    [Qml] public bool ClearAll() => DismissMany(_items.Values.ToArray());

    internal bool DismissMany(IEnumerable<ShellNotification> notifications)
    {
        if (IsDestroyed)
            return false;
        var success = true;
        string? failure = null;
        foreach (var notification in notifications.ToArray())
        {
            if (notification.IsDestroyed || !_items.ContainsKey(notification.NativeId))
            {
                success = false;
                continue;
            }
            try
            {
                _backend.Dismiss(notification.NativeId);
                Interlocked.Increment(ref _revision);
                _items.Remove(notification.NativeId);
                RebuildModels();
                RetainableHook.Release(notification);
            }
            catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
            {
                success = false;
                failure = e.Message;
            }
        }
        if (failure is not null) Fail(failure);
        else if (success) SetField(ref _error, "", nameof(Error));
        return success;
    }

    internal void Fail(string message)
    {
        SetField(ref _error, string.IsNullOrWhiteSpace(message) ? "The Windows notification operation failed." : message, nameof(Error));
        Emit("operationFailed", _error);
    }

    private void Post(Action action)
    {
        void ApplyIfAlive() { if (!IsDestroyed) action(); }
        if (Engine?.Loop is { } loop)
            loop.Post(ApplyIfAlive);
        else
            ApplyIfAlive();
    }

    protected override void OnDestroy()
    {
        _stop.Cancel();
        ServiceSupport.DisposeAfterMonitor(_stop, _monitorTask);
    }
}

/// <summary>
/// Windows listener content, with timestamps in Unix milliseconds for JavaScript Date.
/// A <see cref="RetainableHook"/> lock keeps it after it leaves the center.
/// </summary>
public sealed class ShellNotification : QObject, IRetainable
{
    private readonly NotificationListener _listener;
    private readonly Lock _iconGate = new();
    private IncomingToast _toast;
    private string _appIcon = "";
    private string? _loadedKey;
    private IconHold _hold;
    private int _iconRequest;
    private bool _holdingIcon;
    internal ShellNotification(NotificationListener listener, IncomingToast toast) { _listener = listener; _toast = toast; }
    RetainableHook? IRetainable.RetainHook { get; set; }
    void IRetainable.FinishRetain() => Destroy();
    internal uint NativeId => _toast.Id;
    internal DateTimeOffset CreatedAtValue => _toast.CreatedAt;
    [Qml] public double Id => NativeId;
    [Qml] public string AppId { get { Track(); return _toast.AppId; } }
    [Qml] public string AppName { get { Track(); return _toast.AppName; } }
    /// <summary>A <c>file:///</c> url of the sending app's logo. Empty until the read finishes, and when Windows gives none.</summary>
    [Qml] public string AppIcon { get { Track(); return _appIcon; } }
    [Qml] public string Title { get { Track(); return _toast.Title; } }
    [Qml] public string Body { get { Track(); return _toast.Body; } }
    [Qml] public double CreatedAt { get { Track(); return _toast.CreatedAt.ToUnixTimeMilliseconds(); } }
    [Qml] public double ExpiresAt { get { Track(); return _toast.ExpiresAt?.ToUnixTimeMilliseconds() ?? 0; } }
    [Qml] public bool CanInvoke => false;
    [Qml] public bool Dismiss() => _listener.Dismiss(this);

    private string IconKey => _toast.AppId.Length > 0
        ? _toast.AppId
        : "id:" + _toast.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Starts a logo read when this notification's app does not already have one in flight.</summary>
    internal void SyncIcon()
    {
        if (IsDestroyed || IsDestroying)
            return;
        var source = _toast.Logo;
        var key = source is null ? null : IconKey;
        if (string.Equals(key, _loadedKey, StringComparison.Ordinal))
            return;
        _loadedKey = key;
        Abandon();
        if (source is null || key is null)
            return;
        int request;
        lock (_iconGate)
            request = _iconRequest;
        var label = _toast.AppName.Length > 0 ? _toast.AppName : _toast.AppId.Length > 0 ? _toast.AppId : "a notification";
        var log = Engine?.Log ?? Log.Default;
        var cancellation = _listener.Cancellation;
        // Not the thread pool. Publishing the url has to run even when every pool thread is busy.
        var thread = new Thread(() => Deliver(key, label, source, log, cancellation, request))
        {
            IsBackground = true,
            Name = "Notification icon",
        };
        thread.Start();
    }

    private void Deliver(string key, string label, IAppLogoSource source, Log log, CancellationToken cancellation, int request)
    {
        var read = NotificationAppIcons.Read(key, label, source, log, cancellation);
        PostIcon(() => ApplyIcon(read, request));
    }

    private void ApplyIcon(IconRead read, int request)
    {
        var changed = false;
        lock (_iconGate)
        {
            // A read that finished after dismiss must not land on the dead object, and must not keep the file.
            if (request != _iconRequest || IsDestroyed || IsDestroying)
            {
                NotificationAppIcons.Release(read.Hold);
                return;
            }
            _holdingIcon = true;
            _hold = read.Hold;
            if (_appIcon != read.Url)
            {
                _appIcon = read.Url;
                changed = true;
            }
        }
        if (changed)
            NotifyChanged("appIcon");
    }

    /// <summary>Drops a finished read, and makes an in-flight one release itself when it lands.</summary>
    private void Abandon()
    {
        IconHold release = default;
        var clear = false;
        lock (_iconGate)
        {
            _iconRequest++;
            if (_holdingIcon)
            {
                _holdingIcon = false;
                release = _hold;
                _hold = default;
            }
            clear = _appIcon.Length > 0;
        }
        if (release.Active)
            NotificationAppIcons.Release(release);
        if (clear && !IsDestroyed && !IsDestroying)
            SetField(ref _appIcon, "", nameof(AppIcon));
    }

    private void PostIcon(Action action)
    {
        // Posted even after this listener is gone, so a finished read can still release its file.
        if (_listener.Engine?.Loop is { } loop)
            loop.Post(action);
        else
            action();
    }

    /// <summary>UserNotificationListener exposes no activation arguments or actions for other apps.</summary>
    [Qml] public bool Invoke()
    {
        if (!IsDestroyed)
            _listener.Fail("Windows UserNotificationListener does not support invoking another app's notification. Use Windows Notification Center to activate it.");
        return false;
    }

    internal void Update(IncomingToast toast)
    {
        var old = _toast;
        _toast = toast;
        if (old.AppId != toast.AppId) NotifyChanged("appId");
        if (old.AppName != toast.AppName) NotifyChanged("appName");
        if (old.Title != toast.Title) NotifyChanged("title");
        if (old.Body != toast.Body) NotifyChanged("body");
        if (old.CreatedAt != toast.CreatedAt) NotifyChanged("createdAt");
        if (old.ExpiresAt != toast.ExpiresAt) NotifyChanged("expiresAt");
        SyncIcon();
    }

    protected override void OnDestroy() => Abandon();
}

public sealed class NotificationGroup : QObject
{
    private readonly NotificationListener _listener;
    private string _appId = "";
    private string _appName = "";
    internal NotificationGroup(NotificationListener listener) { _listener = listener; Notifications = new ObjectModel(); Notifications.SetOwner(this); }
    [Qml] public string AppId { get { Track(); return _appId; } }
    [Qml] public string AppName { get { Track(); return _appName; } }
    [Qml] public ObjectModel Notifications { get; }
    [Qml] public bool Dismiss() => !IsDestroyed && _listener.DismissMany(Notifications.Rows.Cast<ShellNotification>().ToArray());
    internal void Update(ShellNotification[] items)
    {
        SetField(ref _appId, items[0].AppId, nameof(AppId));
        SetField(ref _appName, items[0].AppName, nameof(AppName));
        Notifications.Set(items);
    }
}

internal static class NotificationModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Notifications")
            .Type<NotificationListener>("NotificationListener")
            .Type<ToastMessage>("ToastMessage")
            .Type<ToastAction>("ToastAction")
            .Uncreatable<ShellNotification>("Notification")
            .Uncreatable<NotificationGroup>("NotificationGroup")
            .Enum(static (NotificationAccessStatus v) => v.ToString())
            .Enum(static (NotificationState v) => v.ToString())
            .Enum(static (NotificationMode v) => v.ToString());
    }
}
