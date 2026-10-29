import Winshell
import Winshell.Theme
import QtQuick

// Startup: run Winshell when the user signs in, for this account only. Off until the user turns it on. If Windows'
// Startup apps page turned it off, only that page turns it back on, so this page says so and links there.
Column {
  id: root

  spacing: 4

  // Windows' page may have changed it while this one was away.
  onVisibleChanged: if (visible) Startup.refresh()

  SettingsCard {
    id: signIn
    key: "runAtSignIn"
    description: !Startup.available ? "Only a shell started by winshell.exe can run at sign-in"
      : Startup.turnedOffInWindows ? "Turned off in Startup apps in Windows, which can turn it back on"
      : setting.description
    focusTarget: signInSwitch.control

    SettingsSwitch {
      id: signInSwitch
      card: signIn
      name: signIn.title
      enabled: Startup.available
      checked: Startup.runAtSignIn
      onToggled: on => Startup.runAtSignIn = on
    }
  }

  SettingsHeader { text: "Related settings" }

  SettingsCard { key: "startupApps" }
}
