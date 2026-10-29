import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Controls

// About: the version, the config folder and this instance's log, each a click away.
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
      onClicked: Qt.openUrlExternally(Shell.configRoot)
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
