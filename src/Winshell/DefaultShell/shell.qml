//@ pragma ShellId winshell-default
import Winshell
import Winshell.Notifications
// Winshell's default shell. Copy this folder to %APPDATA%\winshell to make it your own.
ShellRoot {
  NotificationListener { id: notifications }
  Variants {
    model: Shell.screens

    Bar {
      required property var modelData
      screen: modelData
      notificationListener: notifications
    }
  }
}
