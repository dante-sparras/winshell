//@ pragma ShellId winshell-default
import Winshell
import Winshell.Input
import Winshell.Notifications
import QtQuick
// Winshell's default shell. Copy this folder to %APPDATA%\winshell to make it your own. Winshell Settings
// (SettingsWindow.qml) keeps its choices in Preferences, and everything here follows them as they change.
ShellRoot {
  id: shell

  // The primary screen, which Shell.screens lists first, for a taskbar on that one only.
  readonly property var mainScreen: Shell.screens[0] ?? null

  function toggleStart() {
    var list = bars.instances
    for (var i = 0; i < list.length; i++) {
      if (list[i].launcherOpen) {
        list[i].closeLauncher()
        return
      }
    }
    mainTaskbar()?.openLauncher()
  }

  // One Windows key for the shell. A second Hotkey on the same sequence would be taken.
  // The tap is the key-up; onPressed is that tap, so onReleased must not toggle as well.
  Hotkey {
    id: startKey
    sequence: Preferences.startHotkey
    onPressed: shell.toggleStart()
  }

  Hotkey {
    id: settingsKey
    sequence: Preferences.settingsHotkey
    onPressed: settings.open("")
  }

  function mainTaskbar() {
    var list = bars.instances
    return list.find(t => t.isMainScreen) || list[0] || null
  }

  // The chords of Windows 11's taskbar, on the main taskbar. While Explorer runs, it keeps these for its own, and each
  // reports that it is taken. Win+T puts the keyboard on the apps, Win+S opens Search, Win+A opens quick settings,
  // Win+N the notification center, and Win+1 to Win+9 and Win+0 the apps in order, as a click does. With Shift, they
  // open a new window. Win+D, Win+Alt+number, Win+X, Alt+Tab and Win+Tab stay with Windows until those surfaces exist.
  Hotkey {
    sequence: "Win+T"
    onPressed: shell.mainTaskbar()?.focusApps()
  }

  Hotkey {
    sequence: "Win+A"
    onPressed: shell.mainTaskbar()?.toggleQuickSettings()
  }

  Hotkey {
    sequence: "Win+N"
    onPressed: shell.mainTaskbar()?.toggleNotifications()
  }

  Hotkey {
    sequence: "Win+S"
    onPressed: shell.mainTaskbar()?.openSearch()
  }

  Variants {
    model: [1, 2, 3, 4, 5, 6, 7, 8, 9, 0]

    QtObject {
      id: appKeys
      required property int modelData
      readonly property int place: modelData === 0 ? 9 : modelData - 1

      property Hotkey open: Hotkey {
        sequence: "Win+" + appKeys.modelData
        onPressed: shell.mainTaskbar()?.taskList.invokeAt(appKeys.place, false)
      }
      property Hotkey openNew: Hotkey {
        sequence: "Win+Shift+" + appKeys.modelData
        onPressed: shell.mainTaskbar()?.taskList.invokeAt(appKeys.place, true)
      }
    }
  }

  NotificationListener { id: notifications }
  TaskbarPins { id: taskbarPins }

  SettingsWindow {
    id: settings
    notificationListener: notifications
    startHotkey: startKey
    settingsHotkey: settingsKey
  }

  Variants {
    id: bars
    model: Preferences.taskbarAllScreens ? Shell.screens : shell.mainScreen ? [shell.mainScreen] : []

    // The taskbar's Start and Search hooks open the launcher. Search focuses its box.
    Taskbar {
      id: taskbar
      required property var modelData
      screen: modelData
      pins: taskbarPins
      notificationListener: notifications
      centered: Preferences.taskbarCentered
      autoHide: Preferences.taskbarAutoHide
      // With one taskbar, every app is on it.
      appsOn: Preferences.taskbarAllScreens ? Preferences.appsOn : "all"
      onSettingsRequested: settings.open("taskbar")
      onUpdatesRequested: settings.open("winshellUpdates")

      readonly property bool launcherOpen: launcher.visible
      function openLauncher() { launcher.open() }
      function closeLauncher() { launcher.close() }
      function openSearch() { launcher.openSearch() }

      startOpen: launcher.visible
      searchOpen: launcher.searchOpen
      onStartRequested: launcher.toggle()
      onSearchRequested: launcher.openSearch()

      // In the middle of the screen over a centered taskbar, as Windows 11 opens Start; from Start otherwise.
      Launcher {
        id: launcher
        anchor.window: taskbar
        anchor.rect.x: taskbar.centered ? taskbar.width / 2 : taskbar.startX
        anchor.rect.y: 0
        anchor.edges: taskbar.centered ? Edges.Top : Edges.Top | Edges.Left
        anchor.gravity: taskbar.centered ? Edges.Top : Edges.Top | Edges.Right
        anchor.margins.top: 12
      }
    }
  }

  // Volume, brightness, media and microphone changes. It sits above the taskbar and does not reserve space.
  OnScreenDisplayHost {}
}
