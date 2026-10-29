# Winshell.Notifications

Reads Windows Notification Center, reports Focus and Do Not Disturb, and sends native toasts. [Windows notifications](../notifications.md) explains access, deployment and what Windows lets a shell do with another app's notification.

`import Winshell.Notifications`

## NotificationListener

A view of Windows Notification Center. Create one with `NotificationListener { }`; each listener has its own view and reads the full Windows snapshot every two seconds. Loading it checks access without prompting.

| Member | Type | Description |
| --- | --- | --- |
| `notifications` | ObjectModel of [Notification](#notification), read-only | Every readable notification, newest first. Empty without access. |
| `groups` | ObjectModel of [NotificationGroup](#notificationgroup), read-only | The notifications in one group per app, newest first. |
| `accessStatus` | int, read-only | A [NotificationAccessStatus](#notificationaccessstatus) value. |
| `focusState` | int, read-only | A [NotificationState](#notificationstate) value: whether a Focus session is on. |
| `notificationMode` | int, read-only | A [NotificationMode](#notificationmode) value: Windows' current notification restriction. |
| `doNotDisturbState` | int, read-only | A [NotificationState](#notificationstate) value: `On` for priority-only or alarms-only, `Off` for unrestricted. |
| `error` | string, read-only | Why access or the last operation failed. |
| `requestAccess()` | method | Asks Windows for access to notifications and returns at once. Call it from a button in a foreground window. Ignored while a request is pending. |
| `clearAll()` | method, returns bool | Dismisses the notifications in this listener's current snapshot. Returns whether every removal succeeded; failed ones stay. |
| `notificationAdded(notification)` | signal | Emitted after the models contain a newly seen notification, including those in the first snapshot. |
| `operationFailed(message)` | signal | Emitted when an access request, a dismissal or `invoke()` fails, with the same text as `error`. |

## ToastMessage

A native toast sent under an installed Windows AppUserModelID. Create one with `ToastMessage { }` and call `send()`.

| Member | Type | Description |
| --- | --- | --- |
| `appId` | string | The AppUserModelID of the sending app's installed Windows identity. Required. |
| `title` | string | The first line. Required. |
| `body` | string | The text under the title, as plain text. |
| `activationUri` | string | An absolute protocol URI Windows opens when the user clicks the toast. Without one, the toast is informational. |
| `tag` | string | An optional Windows replacement identifier, at most 64 characters. |
| `group` | string | An optional Windows replacement group, at most 64 characters. Needs a `tag`. |
| `silent` | bool | Whether the toast plays no sound. |
| `error` | string, read-only | Why the last send failed. Cleared by a send that passes validation. |
| `actions` | list of [ToastAction](#toastaction) | Up to five buttons on the toast, set in the declaration. |
| `send()` | method, returns bool | Sends the toast. Returns whether validation and submission succeeded; Windows can still hold back the banner. |
| `failed(message)` | signal | Emitted when a send fails, at once or when Windows later rejects the toast. |

## ToastAction

A button on a toast. Create it inside `ToastMessage.actions` with `ToastAction { }`.

| Member | Type | Description |
| --- | --- | --- |
| `text` | string | The button's label. Required. |
| `activationUri` | string | The absolute protocol URI Windows opens when the user clicks the button. |

## Notification

A notification in Notification Center, reached from a listener's `notifications` or a group's `notifications`. The object stays the same while its content changes.

| Member | Type | Description |
| --- | --- | --- |
| `id` | real, read-only | The Windows notification id, a full unsigned 32-bit number. |
| `appId` | string, read-only | The AppUserModelID of the app that sent it. Can be empty. |
| `appName` | string, read-only | The sending app's display name. |
| `title` | string, read-only | The first line of text. |
| `body` | string, read-only | The rest of the text. |
| `createdAt` | real, read-only | When it arrived, in Unix milliseconds for `new Date(createdAt)`. |
| `expiresAt` | real, read-only | When it expires, in Unix milliseconds. 0 when Windows supplied no expiry. |
| `canInvoke` | bool, read-only | Always false: Windows gives a shell no way to activate another app's notification. |
| `dismiss()` | method, returns bool | Removes the notification from Windows. Returns true only when Windows accepted the removal. |
| `invoke()` | method, returns bool | Always returns false and sets the listener's `error`. Use Notification Center to activate it. |

## NotificationGroup

The notifications of one app, reached from `NotificationListener.groups`. Notifications without an app identity each get a group of their own.

| Member | Type | Description |
| --- | --- | --- |
| `appId` | string, read-only | The AppUserModelID the group is for. |
| `appName` | string, read-only | The app's display name. |
| `notifications` | ObjectModel of [Notification](#notification), read-only | The app's notifications, newest first. |
| `dismiss()` | method, returns bool | Dismisses the group's current notifications. Returns whether every removal succeeded; failed ones stay. |

## NotificationAccessStatus

The values of `NotificationListener.accessStatus`. `Requesting` lasts while a `requestAccess()` prompt is pending.

Values: `Unspecified`, `Allowed`, `Denied`, `Unavailable`, `Requesting`

## NotificationMode

The values of `NotificationListener.notificationMode`. `Unknown` on Windows builds without the API.

Values: `Unknown`, `Unrestricted`, `PriorityOnly`, `AlarmsOnly`

## NotificationState

The values of `focusState` and `doNotDisturbState`. `Unknown` when Windows does not report the state.

Values: `Unknown`, `Off`, `On`

## Examples

Every readable notification, with a button that dismisses it.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Notifications

Column {
    NotificationListener { id: center }
    Button {
        visible: center.accessStatus !== NotificationAccessStatus.Allowed
        text: "Allow notification access"
        onClicked: center.requestAccess()
    }
    Repeater {
        model: center.notifications
        Button {
            required property var modelData
            text: modelData.appName + ": " + modelData.title
            onClicked: modelData.dismiss()
        }
    }
    Button { text: "Clear all"; onClicked: center.clearAll() }
}
```
