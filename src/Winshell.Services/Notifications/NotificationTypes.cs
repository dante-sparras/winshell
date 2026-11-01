namespace Winshell.Notifications;

public enum NotificationAccessStatus { Unspecified, Allowed, Denied, Unavailable, Requesting }

public enum NotificationState { Unknown, Off, On }

public enum NotificationMode { Unknown = -1, Unrestricted, PriorityOnly, AlarmsOnly }

internal sealed record IncomingToast(uint Id, string AppId, string AppName, string Title, string Body,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt = null, IAppLogoSource? Logo = null);

internal sealed record NotificationSnapshot(NotificationAccessStatus AccessStatus, IReadOnlyList<IncomingToast> Notifications,
    NotificationState Focus, NotificationMode Mode, string Error = "");
