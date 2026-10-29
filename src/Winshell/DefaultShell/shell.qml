//@ pragma ShellId winshell-default
import Winshell
import Winshell.Input
import Winshell.Notifications
// Winshell's default shell. Copy this folder to %APPDATA%\winshell to make it your own.
ShellRoot {
  id: shell

  // One Windows key for the shell. A second Hotkey on the same sequence would be taken.
  // The tap is the key-up; onPressed is that tap, so onReleased must not toggle as well.
  function toggleStart() {
    var list = bars.instances
    for (var i = 0; i < list.length; i++) {
      if (list[i].launcherOpen) {
        list[i].closeLauncher()
        return
      }
    }
    if (list.length > 0)
      list[0].openLauncher()
  }

  Hotkey {
    sequence: "Win"
    onPressed: shell.toggleStart()
  }

  NotificationListener { id: notifications }
  TaskbarPins { id: taskbarPins }

  Variants {
    id: bars
    model: Shell.screens

    // The taskbar's Start and Search hooks open the launcher and Windows search.
    Taskbar {
      id: taskbar
      required property var modelData
      screen: modelData
      pins: taskbarPins
      notificationListener: notifications

      readonly property bool launcherOpen: launcher.visible
      function openLauncher() { launcher.open() }
      function closeLauncher() { launcher.close() }

      startOpen: launcher.visible
      onStartRequested: launcher.toggle()
      onSearchRequested: Shell.execDetached(["explorer.exe", "search-ms:"])

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
