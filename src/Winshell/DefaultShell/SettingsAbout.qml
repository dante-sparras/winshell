import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls

// About: the version and Winshell's updates, the config folder and this instance's log, each a click away, and
// the checks winshell doctor runs. They only read, and list what they found under the card, a fix under each problem.
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
      : Updates.status === UpdateStatus.Ready ? (Updates.error !== "" ? Updates.error
        : "Winshell " + Updates.version + " is ready. Restart Winshell to finish updating")
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

  SettingsCard {
    id: diagnostics
    key: "diagnostics"
    description: !Diagnostics.available ? "Only a shell started by winshell.exe can check its setup"
      : Diagnostics.running ? "Checking…"
      : Diagnostics.summary !== "" ? Diagnostics.summary
      : setting.description
    focusTarget: runDiagnostics

    Button {
      id: runDiagnostics
      objectName: "runDiagnostics"
      text: "Run diagnostics"
      enabled: Diagnostics.available && !Diagnostics.running
      Accessible.name: "Run diagnostics"
      onClicked: Diagnostics.run()
      onActiveFocusChanged: if (activeFocus) diagnostics.reveal()
    }
  }

  // What the last run found: a row a check, as the Windows Settings troubleshooters list theirs.
  Rectangle {
    objectName: "diagnosticsResults"
    width: parent.width
    visible: results.count > 0
    implicitHeight: list.implicitHeight + 16
    radius: Theme.controlCornerRadius
    color: Theme.cardBackgroundFillColorDefault
    border.width: 1
    border.color: Theme.cardStrokeColorDefault

    Column {
      id: list
      x: 16
      y: 8
      width: parent.width - 32

      Repeater {
        id: results
        model: Diagnostics.checks

        RowLayout {
          id: row
          required property var modelData
          readonly property string word: modelData.status === "pass" ? "Passed" : modelData.status === "warn" ? "Warning" : "Failed"
          objectName: "check:" + modelData.id
          width: list.width
          spacing: 12
          Accessible.role: Accessible.StaticText
          Accessible.name: word + ": " + modelData.title + ". " + modelData.message + (modelData.fix !== "" ? " Fix: " + modelData.fix : "")

          // An InfoBar's status icon: a filled circle in the status color behind its glyph.
          Item {
            Layout.alignment: Qt.AlignTop
            Layout.topMargin: 10
            implicitWidth: 16
            implicitHeight: 16
            Accessible.ignored: true

            Text {
              anchors.centerIn: parent
              text: "\uF136"
              font.family: Theme.iconFontFamily
              font.pixelSize: 16
              color: row.modelData.status === "pass" ? Theme.systemFillColorSuccess
                : row.modelData.status === "warn" ? Theme.systemFillColorCaution
                : Theme.systemFillColorCritical
              Accessible.ignored: true
            }
            Text {
              anchors.centerIn: parent
              text: row.modelData.status === "pass" ? "\uF13E" : row.modelData.status === "warn" ? "\uF13C" : "\uF13D"
              font.family: Theme.iconFontFamily
              font.pixelSize: 16
              color: Theme.textFillColorInverse
              Accessible.ignored: true
            }
          }

          Column {
            Layout.fillWidth: true
            Layout.topMargin: 8
            Layout.bottomMargin: 8
            spacing: 2
            Accessible.ignored: true

            Text {
              width: parent.width
              text: row.modelData.title
              font: Theme.typography.bodyStrong
              color: Theme.textFillColorPrimary
              Accessible.ignored: true
            }
            Text {
              width: parent.width
              text: row.modelData.message
              font: Theme.typography.caption
              wrapMode: Text.Wrap
              color: Theme.textFillColorSecondary
              Accessible.ignored: true
            }
            Text {
              objectName: "fix:" + row.modelData.id
              width: parent.width
              visible: row.modelData.fix !== ""
              text: "Fix: " + row.modelData.fix
              font: Theme.typography.caption
              wrapMode: Text.Wrap
              color: Theme.textFillColorPrimary
              Accessible.ignored: true
            }
          }
        }
      }
    }
  }
}
