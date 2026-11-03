namespace Winshell.Notifications;

/// <summary>Whether the shell may read the notifications in Notification Center.</summary>
public enum NotificationAccessStatus
{
    /// <summary>The user has not answered yet, or the first read has not finished.</summary>
    Unspecified,
    /// <summary>The user allowed access.</summary>
    Allowed,
    /// <summary>The user denied access. It can be allowed again in Windows Settings.</summary>
    Denied,
    /// <summary>Windows cannot give access, such as when the shell runs without a package identity.</summary>
    Unavailable,
    /// <summary>An access request is waiting for the user's answer.</summary>
    Requesting,
}

/// <summary>Whether a Windows setting is on.</summary>
public enum NotificationState
{
    /// <summary>Windows does not say, or this version of Windows does not have the setting.</summary>
    Unknown,
    /// <summary>The setting is off.</summary>
    Off,
    /// <summary>The setting is on.</summary>
    On,
}

/// <summary>Which notifications Windows shows, as do not disturb sets it.</summary>
public enum NotificationMode
{
    /// <summary>Windows does not say, or this version of Windows does not report it.</summary>
    Unknown = -1,
    /// <summary>Every notification shows.</summary>
    Unrestricted,
    /// <summary>Only notifications from priority apps and people show.</summary>
    PriorityOnly,
    /// <summary>Only alarms show.</summary>
    AlarmsOnly,
}

internal sealed record IncomingToast(uint Id, string AppId, string AppName, string Title, string Body,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt = null, IAppLogoSource? Logo = null);

internal sealed record NotificationSnapshot(NotificationAccessStatus AccessStatus, IReadOnlyList<IncomingToast> Notifications,
    NotificationState Focus, NotificationMode Mode, string Error = "");
