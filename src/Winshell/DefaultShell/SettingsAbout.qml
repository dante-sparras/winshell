import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Controls

// About: the version and Winshell's updates, the config folder and this instance's log, each a click away.
Column {
  id: root

  spacing: 4

  SettingsCard {
    id: version
    key: "version"
    description: Shell.version
    focusTarget: copy

    Button {
      id: copy
      objectName: "copyVersion"
      text: "Copy"
      Accessible.name: "Copy the version"
      onClicked: Shell.clipboardText = "Winshell " + Shell.version
      onActiveFocusChanged: if (activeFocus) version.reveal()
    }
  }

  // Windows Update's words, for Winshell's own updates. A packaged or source build says who updates it instead.
  SettingsCard {
    id: updates
    key: "winshellUpdates"
    readonly property bool busy: Updates.status === UpdateStatus.Checking || Updates.status === UpdateStatus.Downloading
    description: !Updates.available ? (Updates.packaged
        ? "Updates come from where you installed Winshell, such as the Microsoft Store"
        : "This build of Winshell does not update itself")
      : Updates.status === UpdateStatus.Checking ? "Checking for updates…"
      : Updates.status === UpdateStatus.UpToDate ? "You're up to date"
      : Updates.status === UpdateStatus.Downloading ? "Downloading Winshell " + Updates.version + "…"
      : Updates.status === UpdateStatus.Ready ? "Winshell " + Updates.version + " is ready. Restart Winshell to finish updating"
      : Updates.status === UpdateStatus.Failed ? Updates.error
      : "Get the newest Winshell from GitHub"
    focusTarget: Updates.available ? updateButton : null

    Button {
      id: updateButton
      objectName: "updateNow"
      visible: Updates.available
      enabled: !updates.busy
      text: Updates.status === UpdateStatus.Ready ? "Restart now" : "Check for updates"
      Accessible.name: Updates.status === UpdateStatus.Ready ? "Restart Winshell to update" : "Check for Winshell updates"
      onClicked: Updates.status === UpdateStatus.Ready ? Updates.restart() : Updates.check()
      onActiveFocusChanged: if (activeFocus) updates.reveal()
    }
  }

  SettingsCard {
    id: automatic
    key: "autoUpdate"
    focusTarget: automaticSwitch.control

    SettingsSwitch {
      id: automaticSwitch
      card: automatic
      name: automatic.title
      enabled: Updates.available
      checked: Updates.available && Preferences.autoUpdate
      onToggled: on => Preferences.autoUpdate = on
    }
  }

  SettingsCard {
    id: folder
    key: "configFolder"
    description: Shell.configRoot !== "" ? Shell.configRoot : "This shell has no config folder"
    focusTarget: openFolder

    Button {
      id: openFolder
      objectName: "openConfigFolder"
      text: "Open folder"
      enabled: Shell.configRoot !== ""
      Accessible.name: "Open the config folder"
      onClicked: {
        // A new user has no config folder until the first choice; opening it makes it.
        Preferences.save()
        Qt.openUrlExternally(Shell.configRoot)
      }
      onActiveFocusChanged: if (activeFocus) folder.reveal()
    }
  }

  SettingsCard {
    id: log
    key: "log"
    description: Shell.logPath !== "" ? Shell.logPath : "This instance writes no log"
    focusTarget: openLog

    Button {
      id: openLog
      objectName: "openLog"
      text: "Open log"
      enabled: Shell.logPath !== ""
      Accessible.name: "Open the log"
      onClicked: Qt.openUrlExternally(Shell.logPath)
      onActiveFocusChanged: if (activeFocus) log.reveal()
    }
  }
}
