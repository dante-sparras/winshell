# Microsoft Store listing (en-us)

Paste these into Partner Center's Store listing. Keep them in step with the README. Limits are Partner Center's.

## Product name

Winshell

## Short description (≤ 270 recommended)

A Windows 11 taskbar, Start, quick settings and notification center. A shell you write is a .NET program.

## Description (≤ 10,000)

Winshell is a desktop shell for Windows 11. Out of the box it runs a taskbar, Start, quick settings, a notification center and on-screen displays that look and behave like Windows 11's own, with Mica and Acrylic, your accent color, light and dark mode, Narrator and keyboard support.

The default shell is built in. To change it, or to write your own, you write a .NET project. It builds into one program: a bar, a launcher, a dock. Winshell gives that program Windows' apps, windows, notifications, audio, media, network, Bluetooth, power and more. Running the default shell needs no .NET SDK. Writing a shell does.

Winshell Settings changes the default shell: the taskbar's alignment and auto-hide, which displays have a taskbar, Start's recommendations, quick settings' tiles, hotkeys and more.

Winshell runs alongside Windows. It does not change Windows settings. It leaves Windows' own taskbar in place unless `hideExplorerTaskbar` is set in preferences.toml, which is off until then.

## Product features (up to 20, ≤ 200 each)

- A Windows 11 taskbar with pinned apps and window previews
- Start, with pinned and recommended apps and search
- Quick settings for Wi-Fi, Bluetooth, volume, brightness and more
- A notification center and calendar
- On-screen displays for volume, brightness and media
- Mica, Acrylic, accent color, light and dark mode
- Winshell Settings, laid out like Windows Settings
- Write your own shell as a .NET project; it builds into one program
- Works with Narrator and the keyboard

## Search terms (up to 7)

taskbar, shell, start menu, winui, customization, desktop, windows

## Category

Personalization

## Copyright and trademark info

Copyright (c) Dante Sparrås. MIT License.

## Privacy policy URL

https://github.com/enpaze/winshell/blob/main/docs/privacy.md

## Website

https://github.com/enpaze/winshell

## Support contact info

https://github.com/enpaze/winshell/issues

## Restricted capabilities: why Winshell needs them

Paste the two bullets. Do not paste the note under them.

- **runFullTrust:** Winshell is a desktop shell. It draws its windows with WinUI in a XAML island, reserves screen edges for its taskbar, and reads the running windows and the notification area.
- **unvirtualizedResources:** Winshell keeps its configuration in `%APPDATA%\winshell`, which users edit by hand and share between the Store and portable versions. With file writes virtualized, that folder would land in a copy only the package sees. It also reads the user's real registry, such as the theme and which tray icons show. Registry virtualization would hide those. Sign-in does not use the `Run` key: it is the package's startup task.

Not for pasting. Registry virtualization stays off because the shell reads the user's real registry, not because of sign-in. If review refuses `unvirtualizedResources`: MSIX virtualizes writes only under AppData and HKCU, so the Store build could keep its config in a folder outside AppData and drop this capability, and would then see a virtual registry instead of the user's theme and tray settings.

## Images

- App tile icon (300×300): `packaging/store/logo-300.png`
- Screenshots: at least one desktop screenshot, 1366×768 or larger, PNG. Take them of the default shell on a real desktop.
