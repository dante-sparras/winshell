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
| `AppName` | string, read-only |  |
| `Body` | string, read-only |  |
| `CanInvoke` | bool, read-only | Always false: Windows gives a shell no way to activate another app's notification. |
| `CreatedAt` | DateTimeOffset, read-only |  |
| `ExpiresAt` | DateTimeOffset?, read-only | When it expires, or null when Windows supplied no expiry. |
| `Id` | UInt32, read-only | The Windows notification id. |
| `Title` | string, read-only |  |
| `Dismiss()` | bool | Removes the notification from Windows. Returns true only when Windows accepted the removal. |
| `Invoke()` | bool | UserNotificationListener exposes no activation arguments or actions for other apps. |

## NotificationAccessStatus

| Value | Number | Description |
| --- | --- | --- |
| `Unspecified` | 0 |  |
| `Allowed` | 1 |  |
| `Denied` | 2 |  |
| `Unavailable` | 3 |  |
| `Requesting` | 4 |  |

## NotificationGroup

The notifications of one app. Notifications without an app identity each get a group of their own.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppId` | string, read-only |  |
| `AppName` | string, read-only |  |
| `Notifications` | ObservableCollection of [Notification](#notification), read-only | The app's notifications, newest first. |
| `Dismiss()` | bool | Dismisses the group's current notifications. Returns whether every removal succeeded; failed ones stay. |

## NotificationListener

A view of Windows Notification Center. Making one never prompts for access.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AccessStatus` | [NotificationAccessStatus](#notificationaccessstatus), read-only |  |
| `DoNotDisturbState` | [NotificationState](#notificationstate), read-only | On for priority-only or alarms-only, Off for unrestricted. |
| `Error` | string, read-only | Why access or the last operation failed. |
| `FocusState` | [NotificationState](#notificationstate), read-only | Whether a Focus session is on. |
| `Groups` | ObservableCollection of [NotificationGroup](#notificationgroup), read-only | The notifications in one group per app, newest first. |
| `NotificationMode` | [NotificationMode](#notificationmode), read-only | Windows' current notification restriction. |
| `Notifications` | ObservableCollection of [Notification](#notification), read-only | Every readable notification, newest first. Empty without access. |
| `NotificationAdded` | Action of [Notification](#notification) |  |
| `OperationFailed` | Action of string |  |
| `ClearAll()` | bool | Dismisses this listener's current snapshot. Returns whether every removal succeeded; failed ones stay. |
| `Dispose()` | void |  |
| `RequestAccess()` | void | Asks Windows for access and returns at once. Call from a button in a foreground window. |

## NotificationMode

| Value | Number | Description |
| --- | --- | --- |
| `Unrestricted` | 0 |  |
| `PriorityOnly` | 1 |  |
| `AlarmsOnly` | 2 |  |
| `Unknown` | -1 |  |

## NotificationState

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Off` | 1 |  |
| `On` | 2 |  |

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
| `ToastMessage([IEventLoop](winshell.md#ieventloop) loop)` | constructor |  |
| `Actions` | ObservableCollection of [ToastAction](#toastaction), read-only | Up to five buttons on the toast. |
| `ActivationUri` | string | An absolute protocol URI Windows opens when the user clicks the toast. Without one, the toast is informational. |
| `AppId` | string | The AppUserModelID of the sending app's installed Windows identity. Required. |
| `Body` | string | The text under the title, as plain text. |
| `Error` | string, read-only | Why the last send failed. Cleared by a send that passes validation. |
| `Group` | string | An optional Windows replacement group, at most 64 characters. Needs a Tag. |
| `Silent` | bool | Whether the toast plays no sound. |
| `Tag` | string | An optional Windows replacement identifier, at most 64 characters. |
| `Title` | string | The first line. Required. |
| `Failed` | Action of string |  |
| `Send()` | bool | Sends the toast. Returns whether validation and submission succeeded; Windows can still hold back the banner. |

