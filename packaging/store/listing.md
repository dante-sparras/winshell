# Microsoft Store listing (en-us)

Paste these into Partner Center's Store listing. Keep them in step with the README. Limits are Partner Center's.

## Product name

Winshell

## Short description (≤ 270 recommended)

A toolkit for writing your own Windows 11 desktop shell: a taskbar, launcher or dock, as a .NET program.

## Description (≤ 10,000)

Winshell is a toolkit for writing a desktop shell for Windows 11. You write a .NET project with C# and WinUI 3, and it builds into one program: a bar, a launcher, a dock. Winshell gives that program Windows' apps, windows, notifications, audio, media, network, Bluetooth, power and more, with Mica and Acrylic, your accent color, light and dark mode, Narrator and keyboard support.

Installing Winshell gives you no taskbar. The `winshell` command builds the shell you write, runs it, and starts it again if it crashes. Writing a shell needs the .NET SDK.

Winshell runs alongside Windows. It does not change Windows settings. It leaves Windows' own taskbar in place unless your shell hides it.

## Product features (up to 20, ≤ 200 each)

- Write your own shell as a .NET project; it builds into one program
- Panels, popups and windows in WinUI 3, anchored to screen edges
- Apps, windows, virtual desktops and the notification area
- Notifications, audio, media, network, Bluetooth and power
- Mica, Acrylic, accent color, light and dark mode
- Builds, runs and restarts your shell with the winshell command
- Works with Narrator and the keyboard

## Search terms (up to 7)

taskbar, shell, winui, toolkit, customization, desktop, windows

## Category

Personalization

## Copyright and trademark info

Copyright (c) Dante Sparrås. MIT License.

## Website

https://github.com/enpaze/winshell

## Support contact info

https://github.com/enpaze/winshell/issues

## Restricted capabilities: why Winshell needs them

Paste the two bullets. Do not paste the note under them.

- **runFullTrust:** Winshell builds and runs desktop shells. A shell draws its windows with WinUI in a XAML island, reserves screen edges for its bars, and reads the running windows and the notification area.
- **unvirtualizedResources:** Winshell keeps its configuration in `%APPDATA%\winshell`, which users edit by hand and share between the Store and portable versions. With file writes virtualized, that folder would land in a copy only the package sees. It also reads the user's real registry, such as the theme and which tray icons show. Registry virtualization would hide those. Sign-in does not use the `Run` key: it is the package's startup task.

Not for pasting. Registry virtualization stays off because the shell reads the user's real registry, not because of sign-in. If review refuses `unvirtualizedResources`: MSIX virtualizes writes only under AppData and HKCU, so the Store build could keep its config in a folder outside AppData and drop this capability, and would then see a virtual registry instead of the user's theme and tray settings.

## Images

- App tile icon (300×300): `packaging/store/logo-300.png`
- Screenshots: at least one desktop screenshot, 1366×768 or larger, PNG. Take them of a shell made with `dotnet new winshell` on a real desktop.
