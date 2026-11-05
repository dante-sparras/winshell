using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Windows.UI.Shell;
using Winshell.Updates;

namespace Winshell.Notifications;

internal interface INotificationBackend
{
    Task<NotificationAccessStatus> RequestAccessAsync();
    Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation);
    void Dismiss(uint id);

    Action? WatchToasts(Action changed)
    {
        return null;
    }
}

internal sealed class WindowsNotificationBackend : INotificationBackend
{
    /// <summary>
    /// The toasts of the last read. A poll sees the same toasts, each of which costs Windows milliseconds to name its
    /// app, and an app that changes a toast replaces it, which gives it a new creation time.
    /// </summary>
    private Dictionary<(uint Id, DateTimeOffset Created), IncomingToast> _read = [];

    private readonly ToastChanges _changes = new(ToastChanges.DatabaseStamp, TimeProvider.System);
    private IReadOnlyList<IncomingToast> _toasts = [];

    public async Task<NotificationAccessStatus> RequestAccessAsync()
    {
        return NotificationListenerAccess.Map(await UserNotificationListener.Current.RequestAccessAsync());
    }

    public async Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Focus and notification restriction mode do not require listener permission.
        var focus = ReadFocus();
        var mode = ReadMode();
        try
        {
            var listener = UserNotificationListener.Current;
            var access = NotificationListenerAccess.Map(listener.GetAccessStatus());
            if (access != NotificationAccessStatus.Allowed)
            {
                _changes.Change();
                return new NotificationSnapshot(access, [], focus, mode);
            }

            if (!_changes.Due(out var stamp))
                return new NotificationSnapshot(access, _toasts, focus, mode);
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellation)
                .ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // Access may be revoked while the asynchronous read is in progress.
            access = NotificationListenerAccess.Map(listener.GetAccessStatus());
            if (access != NotificationAccessStatus.Allowed)
            {
                _changes.Change();
                return new NotificationSnapshot(access, [], focus, mode);
            }

            var known = _read;
            Dictionary<(uint, DateTimeOffset), IncomingToast> read = [];
            List<IncomingToast> toasts = [];
            foreach (var notification in notifications)
                try
                {
                    var key = (notification.Id, notification.CreationTime);
                    if (!known.TryGetValue(key, out var toast))
                        toast = ReadToast(notification);
                    read[key] = toast;
                    toasts.Add(toast);
                }
                catch (Exception e) when (IsExpectedFailure(e))
                {
                    // An app can disappear between enumeration and reading its toast.
                    Log.Default.Warn("notifications", e.Message);
                }

            _read = read;
            _toasts = toasts;
            _changes.Read(stamp);
            return new NotificationSnapshot(access, toasts, focus, mode);
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return new NotificationSnapshot(NotificationAccessStatus.Unavailable, [], focus, mode,
                $"Windows notification access is unavailable: {e.Message} Check package identity and the userNotificationListener capability.");
        }
    }

    public void Dismiss(uint id)
    {
        var listener = UserNotificationListener.Current;
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            throw new UnauthorizedAccessException("Windows notification listener access is no longer allowed.");
        listener.RemoveNotification(id);
        _changes.Change();
    }

    /// <summary>
    /// Calls <paramref name="changed"/> when a toast arrives or goes, and returns the call that stops it. Null without
    /// package identity or access, where Windows does not raise the event.
    /// </summary>
    public Action? WatchToasts(Action changed)
    {
        if (!Packaging.IsPackaged)
            return null;
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                return null;
            TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs> handler = (_, _) =>
            {
                _changes.Change();
                changed();
            };
            listener.NotificationChanged += handler;
            return () => listener.NotificationChanged -= handler;
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return null;
        }
    }

    internal static IncomingToast ReadToast(UserNotification notification)
    {
        var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                      ?? notification.Notification.Visual.Bindings.FirstOrDefault();
        var texts = binding?.GetTextElements().Select(t => t.Text).ToArray() ?? [];
        var appId = "";
        var appName = "";
        IAppLogoSource? logo = null;
        try
        {
            var info = notification.AppInfo;
            appId = info.AppUserModelId;
            var display = info.DisplayInfo;
            appName = display.DisplayName;
            // The stream stays closed until the icon thread asks. The toast is already in the snapshot.
            logo = new WindowsAppLogo(display);
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
        }

        return new IncomingToast(notification.Id, appId, appName, texts.FirstOrDefault() ?? "",
            string.Join('\n', texts.Skip(1)), notification.CreationTime, notification.Notification.ExpirationTime,
            logo);
    }

    private static NotificationState ReadFocus()
    {
        try
        {
            if (ApiInformation.IsTypePresent("Windows.UI.Shell.FocusSessionManager") && FocusSessionManager.IsSupported)
                return FocusSessionManager.GetDefault().IsFocusActive ? NotificationState.On : NotificationState.Off;
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
        }

        return NotificationState.Unknown;
    }

    private static NotificationMode ReadMode()
    {
        try
        {
            if (ApiInformation.IsPropertyPresent("Windows.UI.Notifications.ToastNotificationManagerForUser",
                    "NotificationMode"))
                return MapMode((int)ToastNotificationManager.GetDefault().NotificationMode);
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
        }

        return NotificationMode.Unknown;
    }

    internal static NotificationMode MapMode(int mode)
    {
        return mode switch
        {
            0 => NotificationMode.Unrestricted,
            1 => NotificationMode.PriorityOnly,
            2 => NotificationMode.AlarmsOnly,
            _ => NotificationMode.Unknown
        };
    }

    internal static bool IsExpectedFailure(Exception e)
    {
        return e is COMException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException or TypeLoadException;
    }
}

/// <summary>
/// <c>AppDisplayInfo.GetLogo</c> returns a stream reference, not bytes. Opening it blocks, so the caller is
/// the icon thread, never the UI thread.
/// </summary>
internal sealed class WindowsAppLogo(AppDisplayInfo display) : IAppLogoSource
{
    /// <summary>The square size asked of Windows. The picture is whatever asset it matches.</summary>
    private const int Side = 256;

    public Stream? Open(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var reference = display.GetLogo(new Size(Side, Side));
        if (reference is null)
            return null;
#pragma warning disable VSTHRD002 // The icon thread is here to wait.
        var opened = reference.OpenReadAsync().AsTask(cancellation).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        return opened is null ? null : new WinRtReadStream(opened);
    }

    private sealed class WinRtReadStream(IRandomAccessStream inner) : Stream
    {
        private readonly Stream _stream = inner.AsStreamForRead();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
