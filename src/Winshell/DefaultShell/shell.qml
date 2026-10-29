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

  Variants {
    id: bars
    model: Shell.screens

    Bar {
      required property var modelData
      screen: modelData
      notificationListener: notifications
    }
  }
}
