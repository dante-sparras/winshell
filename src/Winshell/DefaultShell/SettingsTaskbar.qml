import QtQuick

// Taskbar: where the apps sit, auto-hide, and which displays have a taskbar. These are the default shell's
// taskbar; Explorer's taskbar has its own settings in Windows.
Column {
  spacing: 4

  SettingsCard {
    id: alignment
    key: "taskbarAlignment"
    focusTarget: alignmentBox

    SettingsComboBox {
      id: alignmentBox
      card: alignment
      name: alignment.title
      options: [[true, "Center"], [false, "Left"]]
      value: Preferences.taskbarCentered
      onPicked: v => Preferences.taskbarCentered = v
    }
  }

  SettingsCard {
    id: autoHide
    key: "taskbarAutoHide"
    focusTarget: autoHideSwitch.control

    SettingsSwitch {
      id: autoHideSwitch
      card: autoHide
      name: autoHide.title
      checked: Preferences.taskbarAutoHide
      onToggled: on => Preferences.taskbarAutoHide = on
    }
  }

  SettingsHeader { text: "Multiple displays" }

  SettingsCard {
    id: allScreens
    key: "taskbarAllScreens"
    focusTarget: allScreensSwitch.control

    SettingsSwitch {
      id: allScreensSwitch
      card: allScreens
      name: allScreens.title
      checked: Preferences.taskbarAllScreens
      onToggled: on => Preferences.taskbarAllScreens = on
    }
  }

  SettingsCard {
    id: appsOn
    key: "taskbarAppsOn"
    focusTarget: appsOnBox
    // With one taskbar, every app is on it.
    enabled: Preferences.taskbarAllScreens

    SettingsComboBox {
      id: appsOnBox
      card: appsOn
      name: appsOn.title
      implicitWidth: 340
      enabled: appsOn.enabled
      options: [["all", "All taskbars"], ["main", "Main taskbar and taskbar where window is open"], ["where", "Taskbar where window is open"]]
      value: Preferences.taskbarAppsOn
      onPicked: v => Preferences.taskbarAppsOn = v
    }
  }

  SettingsHeader { text: "Related settings" }

  SettingsCard { key: "trayIcons" }
}
