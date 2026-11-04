# Windows notifications

`Shell.Current.Notifications` is a `NotificationListener`. The types are `Notification`, `NotificationGroup`, `ToastMessage` and `ToastAction`, in `Winshell.Notifications`. The members are in the [reference](reference/winshell-notifications.md).

## Reading Notification Center

Making a listener checks access and does not prompt. Call `RequestAccess()` from a button in a foreground window: Windows requires the permission request to start on the UI thread. The method returns immediately. `AccessStatus` changes through `Requesting` to `Allowed`, `Denied`, `Unspecified`, or `Unavailable`. A second request while a prompt is pending is ignored. If the user closes the prompt, `Unspecified` permits another request. After a denial, Windows requires the user to grant access in Settings. Repeated requests do not bypass that decision. Winshell never changes the setting itself.

Microsoft's documented deployment requires an app package identity declaring the `uap3:userNotificationListener` capability. The unpackaged CLI does not install that identity or capability. The Microsoft Store package declares it. Some Windows environments already allow access from an unpackaged host. Use the reported access status. Where Windows refuses access, the collections stay empty and `Error` explains the failure. The rest of the shell continues running. See Microsoft's [notification listener documentation](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener).

The listener reads the full Windows snapshot every ten seconds, including notifications already present at startup. Reading the toasts costs Windows about 100 ms of CPU, so a read asks for them again only when Windows' notification database (`wpndatabase.db`, which is undocumented) has changed, after a change event, a dismissal or a change of access, and at least once a minute. A change Windows makes without writing that database, such as a toast that expires, shows within that minute. A failed read tries again after two seconds. It checks access before and after each read. Revocation clears notification content and groups. With package identity and access, Windows also reports each toast that arrives or goes, and the listener reads the snapshot again at once. `Dispose()` stops the reads and ignores work already in flight. Each listener has its own view. Changes made by another listener or by Windows appear on the next snapshot.

`Notifications` is every readable toast, newest first. `Groups` is one group per app, each with `AppId`, `AppName`, and its own `Notifications`. Groups and notifications are ordered by newest first. Groups use the Windows AppUserModelID. Notifications whose app identity is unavailable stay in separate groups. Windows listener APIs do not expose the sender's toast tag and group, so these are app sections.

Each `Notification` exposes `Id`, `AppId`, `AppName`, `AppIcon`, `Title`, `Body`, `CreatedAt`, `ExpiresAt`, and `CanInvoke`. `CreatedAt` is a `DateTimeOffset`. `ExpiresAt` is null when Windows supplied no expiry. `Id` is the Windows notification id. The same object stays while its content changes. A removed notification stays usable so a UI can finish leaving, and its logo is gone by then.

`AppIcon` is a `file:///` url of the sending app's logo. Windows supplies it through [`AppDisplayInfo.GetLogo`](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.appdisplayinfo.getlogo), which returns a stream, not a path. The notification is shown first. The icon can be empty and then change. The stream is read off the UI thread and written to a temporary file Winshell owns, one file per app. A logo over 8 MiB or 4096 by 4096 pixels, or not a PNG, JPEG, GIF, BMP or WebP picture, is ignored. A failed read leaves the icon empty and is logged once for that app. The file is deleted when that app's notifications are gone.

`Dismiss()` returns true only after Windows accepts removal, then removes the item locally. Failure keeps the item visible and updates `Error` and raises `OperationFailed`. `NotificationAdded` is raised after the collections contain a newly seen item, including the first snapshot.

Windows' public `UserNotificationListener` API supplies visual content and removal, and no activation arguments or action buttons for another app's notification. `CanInvoke` is always false. `Invoke()` returns false and sets `Error`. It does not launch the app in place of activating a toast. There are no fabricated actions, inline replies, or urgency hints.

## Sending a toast

`ToastMessage` takes the shell's `Loop`. Set `AppId`, `Title`, and any of `Body`, `ActivationUri`, `Tag`, `Group`, and `Silent`. Add up to five `ToastAction` values to `Actions`, each with `Text` and `ActivationUri`. `Send()` submits the toast. `Failed` is raised with the message when validation fails or Windows rejects the toast later.

`AppId` must identify the sending app's installed Windows identity. For an unpackaged desktop app, its installer must create a Start menu shortcut with the matching AppUserModelID. `ShellOptions.AppId` does not register a Windows toast identity, and Winshell does not register arbitrary ids or send under another app's identity. See [Microsoft's desktop toast prerequisites](https://learn.microsoft.com/en-us/windows/win32/shell/enable-desktop-toast-with-appusermodelid).

`Send()` returns whether validation and submission succeeded. A true return does not guarantee a banner: Windows applies the user's notification preferences and Do Not Disturb. Sending does not need listener permission.

The title must be nonempty. Body text is plain text. XML characters are escaped. `ActivationUri` and each action use an absolute protocol URI. Windows invokes the registered URI handler when the user clicks the toast or button, including when Winshell is no longer running. There is no callback installed in Winshell. Without `ActivationUri`, the toast is informational. `Tag` and `Group` are optional Windows replacement identifiers, each at most 64 characters. A group requires a tag. `Silent` omits toast audio.

## Focus and Do Not Disturb

`FocusState` and `DoNotDisturbState` are each a `NotificationState`: `Unknown`, `Off`, or `On`. `NotificationMode` is a `NotificationMode`: `Unknown`, `Unrestricted`, `PriorityOnly`, or `AlarmsOnly`. Priority or alarms-only maps to Do Not Disturb `On`. Unrestricted maps to `Off`. An unknown result stays `Unknown`.

Focus uses [`FocusSessionManager`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager). Notification restrictions use [`ToastNotificationManagerForUser.NotificationMode`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.toastnotificationmanagerforuser). On a Windows build that does not have them, both stay `Unknown`. Focus does not imply a notification mode. They update on the same ten-second read even when listener access is denied. There is no setter and no change to the system setting.

## The default shell

The clock on the taskbar opens one popup: the notification center above a month calendar. One listener is shared by the clocks on every screen. The button's accessible name adds how many notifications that center is showing, when there is at least one. An app turned off in Winshell Settings is not counted. The time and date the button draws do not include that number. The window uses the material chosen in Winshell Settings, fits the opening screen's work area, takes keyboard focus, and closes with Escape or a click elsewhere.

Notifications are grouped by Windows app identity. Each one shows the regional short time, the same format as the taskbar clock. A notification from another local day keeps the abbreviated weekday in front of that time. Dismiss removes one notification. Clear removes an app's currently displayed notifications. Clear all removes the listener's current snapshot, except an app that is turned off. `NotificationGroup.Dismiss()` and `NotificationListener.ClearAll()` return whether every removal succeeded. They keep failed removals visible, continue with the others, and report the last failure through `Error` and `OperationFailed`. They do not clear unseen notifications. Only **Allow notification access** requests permission.

Toast banners stay Windows' own. `ToastMessage.Send()` asks Windows to show them. The default shell does not draw a second banner for `NotificationAdded`, including notifications already there at startup. Winshell does not move native banners.

The calendar is a six-row Gregorian month. It uses the process's Windows regional format: the first weekday, the weekday labels, and the month, date, and time labels. Week numbers follow that culture's week rule. Monday-first, four-day locales use ISO week years at December and January. The calendar system is Gregorian, including in a locale whose default calendar differs. A change to Windows' regional formats needs a new instance. The grid has 42 days, each with whether it is in the shown month, today, or selected. Month navigation stays inside years 2 through 9998, so the cells of the months beside the shown one still fit. **Week numbers** in the flyout follows `calendarWeekNumbers` in `preferences.toml`. The calendar has no accounts, appointments, or Focus controls.
