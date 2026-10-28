//@ pragma ShellId winshell-default
import Quickshell

// Winshell's default shell. Copy this folder to %USERPROFILE%\.config\winshell to make it your own.
ShellRoot {
  Variants {
    model: Quickshell.screens

    Bar {
      required property var modelData
      screen: modelData
    }
  }
}
