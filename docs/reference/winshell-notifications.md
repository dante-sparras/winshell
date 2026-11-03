# Winshell.Notifications

Notification Center, Focus, and toasts. Reach them from `Shell.Current.Notifications`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## Notification

A notification in Notification Center. The object stays the same while its content changes. It stays usable after it leaves the center, so a UI can finish an exit transition, but its logo is gone by then.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppIcon` | string, read-only | A `file:///` url of the sending app's logo. Empty until the read finishes, and when Windows gives none. |
| `AppId` | string, read-only | The AppUserModelID of the app that sent it. Can be empty. |
| `AppName` | string, read-only | The display name of the app that sent it. Can be empty. |
| `Body` | string, read-only | The lines of text after the title, joined by line breaks. Can be empty. |
| `CanInvoke` | bool, read-only | Always false: Windows gives a shell no way to activate another app's notification. |
| `CreatedAt` | DateTimeOffset, read-only | When the notification was created. |
| `ExpiresAt` | DateTimeOffset?, read-only | When it expires, or null when Windows supplied no expiry. |
| `Id` | uint, read-only | The Windows notification id. |
| `Title` | string, read-only | The first line of text. Can be empty. |
| `Dismiss()` | bool | Removes the notification from Windows. Returns true only when Windows accepted the removal. |
| `Invoke()` | bool | UserNotificationListener exposes no activation arguments or actions for other apps. |

## NotificationAccessStatus

Whether the shell may read the notifications in Notification Center.

| Value | Number | Description |
| --- | --- | --- |
| `Unspecified` | 0 | The user has not answered yet, or the first read has not finished. |
| `Allowed` | 1 | The user allowed access. |
| `Denied` | 2 | The user denied access. It can be allowed again in Windows Settings. |
| `Unavailable` | 3 | Windows cannot give access, such as when the shell runs without a package identity. |
| `Requesting` | 4 | An access request is waiting for the user's answer. |

## NotificationGroup

The notifications of one app. Notifications without an app identity each get a group of their own.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppId` | string, read-only | The AppUserModelID of the app that sent the notifications. Empty when the app has none. |
| `AppName` | string, read-only | The display name of the app that sent the notifications. Can be empty. |
| `Notifications` | ObservableCollection of [Notification](#notification), read-only | The app's notifications, newest first. |
| `Dismiss()` | bool | Dismisses the group's current notifications. Returns whether every removal succeeded; failed ones stay. |

## NotificationListener

A view of Windows Notification Center. Making one never prompts for access.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AccessStatus` | [NotificationAccessStatus](#notificationaccessstatus), read-only | Whether the shell may read the notifications. Ask with RequestAccess. |
| `DoNotDisturbState` | [NotificationState](#notificationstate), read-only | On for priority-only or alarms-only, Off for unrestricted. |
| `Error` | string, read-only | Why access or the last operation failed. |
| `FocusState` | [NotificationState](#notificationstate), read-only | Whether a Focus session is on. |
| `Groups` | ObservableCollection of [NotificationGroup](#notificationgroup), read-only | The notifications in one group per app, newest first. |
| `NotificationMode` | [NotificationMode](#notificationmode), read-only | Windows' current notification restriction. |
| `Notifications` | ObservableCollection of [Notification](#notification), read-only | Every readable notification, newest first. Empty without access. |
| `NotificationAdded` | Action of [Notification](#notification) | Raised after Notifications holds a newly seen notification, including those in the first read. |
| `OperationFailed` | Action of string | An access request, a dismissal or Notification.Invoke failed. The text is Error. |
| `ClearAll()` | bool | Dismisses this listener's current snapshot. Returns whether every removal succeeded; failed ones stay. |
| `Dispose()` | void | Stops watching Notification Center and releases the app logos it read. |
| `RequestAccess()` | void | Asks Windows for access and returns at once. Call from a button in a foreground window. |

## NotificationMode

Which notifications Windows shows, as do not disturb sets it.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | -1 | Windows does not say, or this version of Windows does not report it. |
| `Unrestricted` | 0 | Every notification shows. |
| `PriorityOnly` | 1 | Only notifications from priority apps and people show. |
| `AlarmsOnly` | 2 | Only alarms show. |

## NotificationState

Whether a Windows setting is on.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows does not say, or this version of Windows does not have the setting. |
| `Off` | 1 | The setting is off. |
| `On` | 2 | The setting is on. |

## ToastAction

A button on a toast.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ToastAction()` | constructor |  |
| `ActivationUri` | string | The absolute protocol URI Windows opens when the user clicks the button. |
| `Text` | string | The button's label. Required. |

## ToastMessage

A native toast sent under an installed Windows AppUserModelID.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ToastMessage([IEventLoop](winshell.md#ieventloop) loop)` | constructor | Creates an empty toast whose Failed event is raised on the given event loop. |
| `Actions` | ObservableCollection of [ToastAction](#toastaction), read-only | Up to five buttons on the toast. |
| `ActivationUri` | string | An absolute protocol URI Windows opens when the user clicks the toast. Without one, the toast is informational. |
| `AppId` | string | The AppUserModelID of the sending app's installed Windows identity. Required. |
| `Body` | string | The text under the title, as plain text. |
| `Error` | string, read-only | Why the last send failed. Cleared by a send that passes validation. |
| `Group` | string | An optional Windows replacement group, at most 64 characters. Needs a Tag. |
| `Silent` | bool | Whether the toast plays no sound. |
| `Tag` | string | An optional Windows replacement identifier, at most 64 characters. |
| `Title` | string | The first line. Required. |
| `Failed` | Action of string | A send failed, at once or when Windows later rejected the toast. The text is Error. |
| `Send()` | bool | Sends the toast. Returns whether validation and submission succeeded; Windows can still hold back the banner. |
