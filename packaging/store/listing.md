# Microsoft Store listing (en-us)

Paste these into Partner Center's Store listing. Keep them in step with the README. Limits are Partner Center's.

## Product name

Winshell

## Short description (≤ 270 recommended)

A Windows 11 taskbar, Start, quick settings and notification center you can change, described in QML.

## Description (≤ 10,000)

Winshell is a desktop shell for Windows 11. Out of the box it runs a taskbar, Start, quick settings, a notification center and on-screen displays that look and behave like Windows 11's own, with Mica and Acrylic, your accent color, light and dark mode, Narrator and keyboard support.

Every part of it is a QML file. Change the default shell, or write your own: a bar, a launcher, a dock, widgets. Winshell gives your shell Windows' apps, windows, notifications, audio, media, network, Bluetooth, power and more, and reloads it as you save.

Winshell Settings changes the default shell: the taskbar's alignment and auto-hide, which displays have a taskbar, Start's recommendations, quick settings' tiles, hotkeys and more.

Winshell runs alongside Windows. It does not change Windows settings or remove Windows' own taskbar.

## Product features (up to 20, ≤ 200 each)

- A Windows 11 taskbar with pinned apps and window previews
- Start, with pinned and recommended apps and search
- Quick settings for Wi-Fi, Bluetooth, volume, brightness and more
- A notification center and calendar
- On-screen displays for volume, brightness and media
- Mica, Acrylic, accent color, light and dark mode
- Winshell Settings, laid out like Windows Settings
- Write your own shell in QML; it reloads as you save
- Works with Narrator and the keyboard

## Search terms (up to 7)

taskbar, shell, start menu, qml, customization, desktop, widgets

## Category

Personalization

## Copyright and trademark info

Copyright (c) Dante Sparrås. MIT License.

## Privacy policy URL

https://github.com/dante-sparras/winshell/blob/main/docs/privacy.md

## Website

https://github.com/dante-sparras/winshell

## Support contact info

https://github.com/dante-sparras/winshell/issues

## Restricted capabilities: why Winshell needs them

Paste the two bullets. Do not paste the note under them.

- **runFullTrust:** Winshell is a desktop shell. It draws its own windows with DirectComposition, reserves screen edges for its taskbar, and reads the running windows and the notification area.
- **unvirtualizedResources:** Winshell keeps its configuration in `%APPDATA%\winshell`, which users edit by hand and share between the Store and portable versions. With file writes virtualized, that folder would land in a copy only the package sees. **Run Winshell when I sign in** writes the user's `Run` key. Registry virtualization would hide that entry from Windows, so the package would not start at sign-in.

Not for pasting. After #142 moves the Store build's sign-in entry to a startup task, check whether registry virtualization can stay on, and if it can, shorten the second bullet to the config folder. If review refuses `unvirtualizedResources`: MSIX virtualizes writes only under AppData and HKCU, so the Store build could keep its config in a folder outside AppData and drop this capability. The second bullet describes the package as it is now, with the `Run` key, not that later state.

## Images

- App tile icon (300×300): `packaging/store/logo-300.png`
- Screenshots: at least one desktop screenshot, 1366×768 or larger, PNG. Take them of the default shell on a real desktop.
