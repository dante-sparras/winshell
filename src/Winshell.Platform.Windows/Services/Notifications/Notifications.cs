using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Winshell.Platform.Windows.Services;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.Notifications;

public enum NotificationUrgency
{
    Low = 0,
    Normal = 1,
    Critical = 2,
}

public enum NotificationCloseReason
{
    Expired = 1,
    Dismissed = 2,
    CloseRequested = 3,
}

/// <summary>One Windows toast, reduced to the fields Quickshell's <c>Notification</c> exposes.</summary>
internal sealed record IncomingToast(
    uint NativeId,
    string AppName,
    string Summary,
    string Body,
    string Image,
    string DesktopEntry,
    double ExpireTimeout);

/// <summary>
/// Quickshell's <c>NotificationServer</c>. Windows has no desktop-notification bus, so this reads the toasts
/// Action Center already has. An unpackaged shell is usually refused; the types still load and the refusal is logged.
/// The capability flags are kept for configs and do not change which toasts Windows delivers.
/// </summary>
[QmlSignal("notification", "notification")]
public sealed class NotificationServer : QObject
{
    private bool _keepOnReload = true;
    private bool _persistenceSupported;
    private bool _bodySupported = true;
    private bool _bodyMarkupSupported;
    private bool _bodyHyperlinksSupported;
    private bool _bodyImagesSupported;
    private bool _actionsSupported;
    private bool _actionIconsSupported;
    private bool _imageSupported;
    private bool _inlineReplySupported;
    private string[] _extraHints = [];
    private HashSet<uint> _prior = [];
    private bool _accepting;

    private const string Category = "quickshell.service.notifications";

    public NotificationServer()
        : this(listen: true)
    {
    }

    internal NotificationServer(bool listen)
    {
        TrackedNotifications = new ObjectModel();
        TrackedNotifications.SetOwner(this);
        if (listen)
            ToastHub.Join(this);
    }

    [Qml] public ObjectModel TrackedNotifications { get; }

    [Qml]
    public bool KeepOnReload
    {
        get { Track(); return _keepOnReload; }
        set
        {
            if (value == _keepOnReload)
                return;
            if (_accepting)
            {
                (Engine?.Log ?? Log.Default).Error(Category, "Cannot set NotificationServer.keepOnReload after the server has been started.");
                return;
            }
            SetField(ref _keepOnReload, value);
        }
    }

    [Qml] public bool PersistenceSupported { get { Track(); return _persistenceSupported; } set => SetField(ref _persistenceSupported, value); }

    [Qml] public bool BodySupported { get { Track(); return _bodySupported; } set => SetField(ref _bodySupported, value); }

    [Qml] public bool BodyMarkupSupported { get { Track(); return _bodyMarkupSupported; } set => SetField(ref _bodyMarkupSupported, value); }

    [Qml] public bool BodyHyperlinksSupported { get { Track(); return _bodyHyperlinksSupported; } set => SetField(ref _bodyHyperlinksSupported, value); }

    [Qml] public bool BodyImagesSupported { get { Track(); return _bodyImagesSupported; } set => SetField(ref _bodyImagesSupported, value); }

    [Qml] public bool ActionsSupported { get { Track(); return _actionsSupported; } set => SetField(ref _actionsSupported, value); }

    [Qml] public bool ActionIconsSupported { get { Track(); return _actionIconsSupported; } set => SetField(ref _actionIconsSupported, value); }

    [Qml] public bool ImageSupported { get { Track(); return _imageSupported; } set => SetField(ref _imageSupported, value); }

    [Qml] public bool InlineReplySupported { get { Track(); return _inlineReplySupported; } set => SetField(ref _inlineReplySupported, value); }

    [Qml] public string[] ExtraHints { get { Track(); return _extraHints; } set => SetField(ref _extraHints, value ?? []); }

    internal void Deliver(IncomingToast toast, int id, bool lastGeneration)
    {
        if (IsDestroyed)
            return;
        var notification = new ShellNotification(this, toast, id, lastGeneration) { Engine = Engine };
        notification.Actions.Engine = Engine;
        notification.SetOwner(this);
        Emit("notification", notification);
        if (!notification.Tracked)
            notification.Destroy();
    }

    /// <summary>
    /// Offers one cached toast. A toast that was already there when this server was created is the previous
    /// generation, and it is dropped when <see cref="KeepOnReload"/> is false.
    /// </summary>
    internal void Offer(IncomingToast toast, int id, bool prior)
    {
        if (prior && !_keepOnReload)
            return;
        Deliver(toast, id, lastGeneration: prior);
    }

    internal void Begin(IEnumerable<uint> prior) => _prior = [.. prior];

    internal bool Accepting => _accepting;

    internal void MarkAccepting() => _accepting = true;

    internal bool WasPrior(uint nativeId) => _prior.Contains(nativeId);

    public override void ComponentComplete()
    {
        TrackedNotifications.Engine = Engine;
        ToastHub.Accept(this);
    }

    internal void RemoteClosed(int id)
    {
        if (Find(id) is { } notification)
            notification.Close(NotificationCloseReason.CloseRequested, tellWindows: false);
    }

    internal void Track(ShellNotification notification)
    {
        if (!TrackedNotifications.Rows.Contains(notification))
            TrackedNotifications.Add(notification);
    }

    internal void Untrack(ShellNotification notification) => TrackedNotifications.Remove(notification);

    internal ShellNotification? Find(int id)
    {
        foreach (var row in TrackedNotifications.Rows)
        {
            if (row is ShellNotification notification && notification.Id == id)
                return notification;
        }
        return null;
    }

    internal void Post(Action action)
    {
        if (Engine?.Loop is { } loop)
            loop.Post(action);
        else
            action();
    }

    protected override void OnDestroy() => ToastHub.Leave(this);
}

/// <summary>Quickshell's <c>NotificationAction</c>. This listener does not receive toast buttons, so none are created.</summary>
public sealed class NotificationAction : QObject
{
    internal NotificationAction(string identifier, string text)
    {
        Identifier = identifier;
        Text = text;
    }

    [Qml] public string Identifier { get; }

    [Qml] public string Text { get; }

    [Qml]
    public void Invoke()
    {
    }
}

/// <summary>Quickshell's <c>Notification</c> for one Windows toast.</summary>
[QmlSignal("closed", "reason")]
public sealed class ShellNotification : QObject, IRetainable
{
    private readonly NotificationServer _server;
    private readonly uint _nativeId;
    private bool _tracked;
    private bool _closed;

    RetainableHook? IRetainable.RetainHook { get; set; }

    void IRetainable.FinishRetain() => Destroy();

    internal ShellNotification(NotificationServer server, IncomingToast toast, int id, bool lastGeneration)
    {
        _server = server;
        _nativeId = toast.NativeId;
        Id = id;
        AppName = toast.AppName;
        Summary = toast.Summary;
        Body = toast.Body;
        Image = toast.Image;
        DesktopEntry = toast.DesktopEntry;
        ExpireTimeout = toast.ExpireTimeout;
        LastGeneration = lastGeneration;
        Hints = new Dictionary<string, object?>
        {
            ["urgency"] = (int)NotificationUrgency.Normal,
            ["desktop-entry"] = toast.DesktopEntry,
        };
        Actions = new ObjectModel();
        Actions.SetOwner(this);
    }

    [Qml] public int Id { get; }

    [Qml] public string AppName { get; }

    [Qml] public string Summary { get; }

    [Qml] public string Body { get; }

    /// <summary>Windows does not give another app's icon to an unpackaged listener.</summary>
    [Qml] public string AppIcon => "";

    [Qml] public string Image { get; }

    [Qml] public string DesktopEntry { get; }

    [Qml] public double ExpireTimeout { get; }

    [Qml] public bool LastGeneration { get; }

    [Qml] public int Urgency => (int)NotificationUrgency.Normal;

    [Qml] public bool Transient => false;

    [Qml] public bool Resident => false;

    [Qml] public bool HasActionIcons => false;

    [Qml] public bool HasInlineReply => false;

    [Qml] public string InlineReplyPlaceholder => "";

    [Qml] public ObjectModel Actions { get; }

    [Qml] public Dictionary<string, object?> Hints { get; }

    [Qml]
    public bool Tracked
    {
        get { Track(); return _tracked; }
        set
        {
            if (value == _tracked)
                return;
            if (!value)
            {
                Dismiss();
                return;
            }
            _tracked = true;
            _server.Track(this);
            NotifyChanged("tracked");
        }
    }

    [Qml]
    public void Dismiss() => Close(NotificationCloseReason.Dismissed, tellWindows: true);

    [Qml]
    public void Expire() => Close(NotificationCloseReason.Expired, tellWindows: true);

    [Qml]
    public void SendInlineReply(string replyText)
    {
        _ = replyText;
    }

    internal void Close(NotificationCloseReason reason, bool tellWindows)
    {
        if (_closed)
            return;
        _closed = true;
        if (_tracked)
        {
            _tracked = false;
            _server.Untrack(this);
            NotifyChanged("tracked");
        }
        if (tellWindows)
            ToastHub.Remove(_nativeId);
        Emit("closed", (int)reason);
        RetainableHook.Release(this);
    }
}

/// <summary>The one listener for every <see cref="NotificationServer"/> in the process.</summary>
internal static class ToastHub
{
    private const string Category = "quickshell.service.notifications";
    private static readonly object Gate = new();
    private static readonly List<NotificationServer> Servers = [];
    private static readonly Dictionary<uint, (IncomingToast Toast, int Id)> Current = [];
    private static UserNotificationListener? _listener;
    private static bool _started;
    private static int _nextId;
    private static int _pollFaults;

    public static void Join(NotificationServer server)
    {
        lock (Gate)
        {
            Servers.Add(server);
            // Remember what Action Center already held. Those toasts are the previous generation.
            server.Begin(Current.Keys);
            if (!_started)
            {
                _started = true;
                ServiceSupport.Run(Category, StartAsync);
            }
        }
    }

    /// <summary>The server's bindings have run, so it can take the toasts gathered so far.</summary>
    public static void Accept(NotificationServer server)
    {
        List<(IncomingToast Toast, int Id)> replay;
        lock (Gate)
        {
            server.MarkAccepting();
            replay = [.. Current.Values];
        }
        foreach (var (toast, id) in replay)
            server.Offer(toast, id, server.WasPrior(toast.NativeId));
    }

    public static void Leave(NotificationServer server)
    {
        lock (Gate)
            Servers.Remove(server);
    }

    public static void Remove(uint nativeId)
    {
        UserNotificationListener? listener;
        lock (Gate)
        {
            Current.Remove(nativeId);
            listener = _listener;
        }
        if (listener is null)
            return;
        try
        {
            listener.RemoveNotification(nativeId);
        }
        catch (Exception e) when (IsListenerFailure(e))
        {
            Log.Default.Warn(Category, Describe(e));
        }
    }

    private static async Task StartAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Log.Default.Warn(Category, "Windows did not let the shell read notifications. An unpackaged app is refused the userNotificationListener capability, so NotificationServer stays empty.");
                return;
            }
            lock (Gate)
                _listener = listener;
            // An unpackaged app can read toasts, but subscribing to changes returns 0x80070490 with an empty message.
            var pushed = TrySubscribe(listener);
            await SnapshotAsync(listener);
            if (pushed)
                return;
            Log.Default.Info(Category, "Windows will not push notification changes to this app, so NotificationServer polls Action Center.");
            await PollAsync(listener);
        }
        catch (Exception e) when (IsListenerFailure(e))
        {
            Log.Default.Warn(Category, Describe(e));
        }
    }

    private static bool TrySubscribe(UserNotificationListener listener)
    {
        try
        {
            listener.NotificationChanged += (_, args) => OnChanged(listener, args);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException e) when (e.HResult == unchecked((int)0x80070490))
        {
            return false;
        }
    }

    private static async Task PollAsync(UserNotificationListener listener)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            try
            {
                await SnapshotAsync(listener).ConfigureAwait(false);
            }
            catch (Exception e) when (IsListenerFailure(e))
            {
                if (Interlocked.Increment(ref _pollFaults) == 1)
                    Log.Default.Warn(Category, Describe(e));
            }
        }
    }

    private static async Task SnapshotAsync(UserNotificationListener listener)
    {
        var existing = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        var live = new HashSet<uint>();
        foreach (var toast in existing)
        {
            live.Add(toast.Id);
            Publish(Read(toast));
        }
        List<int> closed = [];
        lock (Gate)
        {
            foreach (var nativeId in Current.Keys.ToArray())
            {
                if (live.Contains(nativeId))
                    continue;
                closed.Add(Current[nativeId].Id);
                Current.Remove(nativeId);
            }
        }
        foreach (var id in closed)
            Each(server => server.RemoteClosed(id));
    }

    private static void OnChanged(UserNotificationListener listener, UserNotificationChangedEventArgs args)
    {
        try
        {
            switch (args.ChangeKind)
            {
                case UserNotificationChangedKind.Added:
                    Publish(Read(listener.GetNotification(args.UserNotificationId)));
                    break;
                case UserNotificationChangedKind.Removed:
                    int? id;
                    lock (Gate)
                    {
                        id = Current.TryGetValue(args.UserNotificationId, out var known) ? known.Id : null;
                        Current.Remove(args.UserNotificationId);
                    }
                    if (id is { } closed)
                        Each(server => server.RemoteClosed(closed));
                    break;
            }
        }
        catch (Exception e) when (IsListenerFailure(e))
        {
            Log.Default.Warn(Category, Describe(e));
        }
    }

    private static void Publish(IncomingToast? toast)
    {
        if (toast is null)
            return;
        int id;
        List<NotificationServer> servers;
        lock (Gate)
        {
            if (Current.ContainsKey(toast.NativeId))
                return;
            id = ++_nextId;
            Current[toast.NativeId] = (toast, id);
            servers = [.. Servers.Where(server => server.Accepting)];
        }
        foreach (var server in servers)
            server.Post(() => server.Offer(toast, id, prior: false));
    }

    private static void Each(Action<NotificationServer> action)
    {
        List<NotificationServer> servers;
        lock (Gate)
            servers = [.. Servers];
        foreach (var server in servers)
            server.Post(() => action(server));
    }

    private static IncomingToast? Read(UserNotification notification)
    {
        try
        {
            var summary = "";
            var body = "";
            var image = "";
            var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                ?? notification.Notification.Visual.Bindings.FirstOrDefault();
            if (binding is not null)
            {
                var texts = binding.GetTextElements().ToList();
                summary = texts.ElementAtOrDefault(0)?.Text ?? "";
                body = string.Join('\n', texts.Skip(1).Select(t => t.Text)).Trim();
            }
            var app = "";
            var entry = "";
            try
            {
                app = notification.AppInfo.DisplayInfo.DisplayName;
                entry = notification.AppInfo.AppUserModelId;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
            var timeout = 0.0;
            if (notification.Notification.ExpirationTime is { } expires)
                timeout = Math.Max(0, (expires - notification.CreationTime).TotalSeconds);
            return new IncomingToast(notification.Id, app, summary, body, image, entry, timeout);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            Log.Default.Warn(Category, Describe(e));
            return null;
        }
    }

    private static bool IsListenerFailure(Exception e) =>
        e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException;

    private static string Describe(Exception e) =>
        string.IsNullOrWhiteSpace(e.Message) ? $"{e.GetType().Name} (0x{e.HResult:X8})" : e.Message;
}

internal static class NotificationModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Quickshell.Services.Notifications")
            .Type<NotificationServer>("NotificationServer")
            .Uncreatable<ShellNotification>("Notification")
            .Uncreatable<NotificationAction>("NotificationAction")
            .Enum(static (NotificationUrgency u) => u.ToString())
            .Enum(static (NotificationCloseReason r) => r.ToString());
    }
}
