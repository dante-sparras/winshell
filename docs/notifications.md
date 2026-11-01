# Windows notifications

`import Winshell.Notifications` provides `NotificationListener`, `Notification`, `NotificationGroup`, `ToastMessage` and `ToastAction`.

## Reading Notification Center

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Notifications

Item {
    NotificationListener { id: center }
    Button {
        text: center.accessStatus === NotificationAccessStatus.Requesting
            ? "Waiting for Windows…" : "Allow notification access"
        enabled: center.accessStatus !== NotificationAccessStatus.Requesting
        onClicked: center.requestAccess()
    }
    // Bind a list to center.notifications, or center.groups for app sections.
    // Show center.error when access is denied or unavailable.
}
```

Loading the listener checks access without prompting. Call `requestAccess()` from a button in a foreground window: Windows requires the permission request to start on the UI thread. The method returns immediately; `accessStatus` changes through `Requesting` to `Allowed`, `Denied`, `Unspecified`, or `Unavailable`. Duplicate requests while a prompt is pending are ignored. If the user closes the prompt, `Unspecified` permits another request. After denial, Windows requires the user to grant access in Settings; repeated requests do not bypass that decision. Winshell never changes the setting itself.

Microsoft's documented deployment requires an app package identity declaring the `uap3:userNotificationListener` capability. The unpackaged CLI does not install that identity or capability; the Microsoft Store package declares it. Some Windows environments already allow access from an unpackaged host; always use the reported access status. Where Windows refuses access, the models stay empty and `error` explains the failure; the rest of the shell continues running. Packaging and capability deployment are outside this module. See Microsoft's [notification listener documentation](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener).

The listener reconciles the full Windows snapshot every ten seconds, including notifications already present at startup or after a QML reload. It checks access before and after each read. Revocation clears notification content and groups. With package identity and access, Windows also reports each toast that arrives or goes, and the listener reads the snapshot again at once. Destroying the listener stops polling and ignores work already in flight. Each listener has its own view; changes made by another listener or Windows appear on the next snapshot.

`notifications` is an `ObjectModel`, automatically populated with all readable toasts. `groups` is an `ObjectModel` of app groups, each with `appId`, `appName`, and its own `notifications` model. Both groups and notifications are ordered by newest notification first, with ID as the tie breaker. Groups use the Windows AppUserModelID, not the app display name; notifications whose app identity is unavailable stay in separate groups. Windows listener APIs do not expose the sender's toast tag/group, so these are app sections rather than the sender's internal groups.

Each `Notification` exposes read-only `id`, `appId`, `appName`, `appIcon`, `title`, `body`, `createdAt`, `expiresAt`, and `canInvoke`. Timestamps are Unix milliseconds for `new Date(notification.createdAt)`; `expiresAt === 0` means Windows supplied no expiry. IDs preserve the full Windows unsigned 32-bit ID. Existing objects stay stable while their content changes. Removed notifications and empty groups are destroyed.

`appIcon` is a `file:///` url of the sending app's logo. Windows supplies it through [`AppDisplayInfo.GetLogo`](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.appdisplayinfo.getlogo), which returns a stream reference, not a path. The notification is shown first; the icon can be empty and then change. The stream is read off the UI thread and written to a temporary file Winshell owns, one file per app. A logo over 8 MiB or 4096 by 4096 pixels, or not a PNG, JPEG, GIF, BMP or WebP picture, is ignored. A failed read leaves the icon empty and is logged once for that app. The file is deleted when that app's notifications are gone. Quickshell would also try a desktop entry's icon. Windows has no desktop entry, so there is no second lookup.

`dismiss()` returns `true` only after Windows accepts removal, then removes the item locally. Failure keeps the item visible and updates the listener's `error` and `operationFailed(message)` signal. `notificationAdded(notification)` is emitted after the models contain a newly observed item, including the initial snapshot.

Windows' public `UserNotificationListener` API supplies visual content and removal, but no activation arguments or action buttons for another app's notification. Consequently `canInvoke` is `false`, and `invoke()` returns `false` with an explicit error; it does not substitute launching the app for activating a toast. Use Windows Notification Center to invoke incoming notifications. There are no fabricated actions, inline replies, urgency hints, or mutable capability flags.

## Sending native toasts

```qml
import Winshell.Notifications

ToastMessage {
    id: message
    appId: "Your.Installed.AppUserModelID"
    title: "Build finished"
    body: "Open the report"
    activationUri: "https://example.com/report"
    tag: "build"
    group: "reports"
    silent: true
    actions: [
        ToastAction { text: "View report"; activationUri: "https://example.com/report" }
    ]
    // Call message.send() in response to your application's event.
    onFailed: function(message) { console.warn(message) }
}
```

`appId` must identify the sending app's installed Windows identity. For an unpackaged desktop app, its installer must create a Start menu shortcut with the matching AppUserModelID. `Shell.appId` alone does not register a Windows toast identity, and Winshell does not register arbitrary IDs or send under another app's identity. See [Microsoft's desktop toast prerequisites](https://learn.microsoft.com/en-us/windows/win32/shell/enable-desktop-toast-with-appusermodelid).

`send()` submits a native `ToastGeneric` notification and returns whether immediate validation and submission succeeded. A successful return does not guarantee a banner: Windows applies the user's notification preferences and Do Not Disturb rules. A failure, whether `send()` returns `false` or Windows rejects the toast later, updates `error` and emits `failed(message)` on the engine loop. Sending is independent of listener permission.

The title must be nonempty. Body text is plain text; XML characters are escaped. `activationUri` and up to five `ToastAction` entries use absolute protocol URIs. Windows invokes the registered URI handler when the user clicks the toast or button, including when Winshell is no longer running. No arbitrary JavaScript callback or background COM activation registration is installed. Without `activationUri`, the toast is informational; there is no Winshell activation handler. `tag` and `group` are optional Windows replacement identifiers, each at most 64 characters; a group requires a tag. `silent` omits toast audio. Native sends in automated tests are replaced by a fake sender, so the suite creates no notifications.

## Focus and Do Not Disturb

The listener exposes read-only `focusState` and `doNotDisturbState`, each a `NotificationState` (`Unknown`, `Off`, `On`). `notificationMode` is a `NotificationMode` (`Unknown`, `Unrestricted`, `PriorityOnly`, `AlarmsOnly`). Priority or alarms-only restrictions map to Do Not Disturb `On`; unrestricted maps to `Off`. Unknown API results remain `Unknown` rather than claiming that notifications are unrestricted.

Focus uses [`FocusSessionManager`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager). Notification restrictions use [`ToastNotificationManagerForUser.NotificationMode`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.toastnotificationmanagerforuser). These APIs are available only on sufficiently recent Windows builds; Winshell checks availability at runtime and reports `Unknown` on older or unsupported systems. The two states are separate: Focus does not imply a particular notification mode. They update on the same ten-second interval even when listener access is denied. No setter, Focus session command, registry fallback, or system setting change is exposed.

## Default notification center and calendar

The default configuration's clock button opens `CalendarFlyout.qml`, which combines `NotificationCenter.qml` and `CalendarView.qml`. One listener is shared by the clock hooks on all screens. The button's accessible name adds how many notifications that center is showing, when there is at least one; an app turned off in Winshell Settings is not counted. The time and date the button draws do not include that number. The window uses Acrylic, Fluent controls and theme tokens, fits the opening screen's work area, takes keyboard focus, and closes with Escape, Close, or a click elsewhere. Tab and Shift+Tab reach its controls; Enter or Space activates them. Scrolling follows keyboard focus, including on small work areas. Calendar dates expose full regional date names, Today and selection descriptions to UI Automation.

Notifications are grouped by Windows app identity. Each one shows the regional short time, the same format as the taskbar clock. A notification from another local day keeps the abbreviated weekday in front of that time. Dismiss removes one notification, Clear removes an app's currently displayed notifications, and Clear all removes the listener's current snapshot. `NotificationGroup.dismiss()` and `NotificationListener.clearAll()` return whether every removal succeeded. They keep failed removals visible, continue with other removals, and report the last failure through `error` and `operationFailed`. They do not clear unseen notifications or call Windows' global history clear. Permission, unavailable access, empty history and unknown Focus/Do Not Disturb states remain distinct. Only the explicit Allow notification access button requests permission. Incoming notifications have no fabricated activation action.

Toast popups remain native Windows banners: `ToastMessage.send()` already asks Windows to show them at the Windows notification position above the taskbar and apply sound, duration, monitor selection and Do Not Disturb policy. The configuration does not replay `notificationAdded` as a second banner, including notifications recovered on startup/reload. Winshell does not move native banners or replace Explorer's notification UI.

`CalendarMonth` in `Winshell` supplies a six-row Gregorian month grid. `date` is a local civil date/time, `monthOffset` navigates relative to its month, and `reset()` returns to today and selects it. `localeName` defaults to the process's Windows regional format and can be overridden per instance without changing system settings. `firstDayOfWeek`, `weekdayLabels`, `monthLabel`, `dateLabel`, `timeLabel` and `shortDateLabel` use that regional format. `weekNumbers` follows its calendar week rule; Monday-first/four-day locales use ISO week years at December/January boundaries. The calendar system is explicitly Gregorian, including in locales whose default calendar differs. Runtime changes to Windows regional formats require a new instance.

`days` contains 42 stable `CalendarDay` objects with `date`, `label`, `fullDate`, `inMonth`, `isToday` and `isSelected`. `selectDay(index)` selects a displayed date; `selectedDate` is read-only. Invalid indexes are ignored. The Week numbers checkbox changes only this configuration's view; `CalendarFlyout.showWeekNumbers` can set its initial value. Month navigation is bounded to years 2–9998 to leave room for adjacent-month cells. This is a month/date view, without calendar accounts, appointments or Focus controls.
