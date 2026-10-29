import Winshell
import Winshell.Theme
import QtQuick

// Startup: run Winshell when the user signs in, for this account only. Off until the user turns it on. If Windows'
// Startup apps page turned it off, only that page turns it back on, so this page shows it off, says so and links
// there. An entry that starts another winshell.exe is off here too; turning it on points it at this one.
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
      : Startup.registeredElsewhere ? "Set to start Winshell from another folder or another way. Turn it on to start this one"
      : setting.description
    focusTarget: signInSwitch.control

    SettingsSwitch {
      id: signInSwitch
      card: signIn
      name: signIn.title
      enabled: Startup.available && !Startup.turnedOffInWindows
      checked: Startup.runAtSignIn && !Startup.turnedOffInWindows
      onToggled: on => Startup.runAtSignIn = on
    }
  }

  SettingsHeader { text: "Related settings" }

  SettingsCard { key: "startupApps" }
}
