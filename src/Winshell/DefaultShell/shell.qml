//@ pragma ShellId winshell-default
import Winshell
// Winshell's default shell. Copy this folder to %USERPROFILE%\.config\winshell to make it your own.
ShellRoot {
  Variants {
    model: Shell.screens

    Bar {
      required property var modelData
      screen: modelData
    }
  }
}
