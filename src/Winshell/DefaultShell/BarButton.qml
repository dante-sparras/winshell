import Quickshell.Windows
import QtQuick

// A bar button that looks like a Windows 11 taskbar button: a subtle fill on hover and press.
MouseArea {
  id: root

  property string icon: ""
  property string label: ""

  implicitWidth: content.implicitWidth + 20
  implicitHeight: 32
  hoverEnabled: true

  Rectangle {
    anchors.fill: parent
    radius: 4
    color: root.pressed ? WindowsTheme.subtleFillTertiary : root.containsMouse ? WindowsTheme.subtleFillSecondary : "transparent"
    border.width: root.containsMouse ? 1 : 0
    border.color: WindowsTheme.controlStroke

    Behavior on color { ColorAnimation { duration: 83 } }
  }

  Row {
    id: content
    anchors.centerIn: parent
    spacing: 8

    Text {
      visible: root.icon !== ""
      anchors.verticalCenter: parent.verticalCenter
      text: root.icon
      font.family: WindowsTheme.iconFontFamily
      font.pixelSize: 16
      color: WindowsTheme.textPrimary
    }

    Text {
      visible: root.label !== ""
      anchors.verticalCenter: parent.verticalCenter
      text: root.label
      horizontalAlignment: Text.AlignRight
      font.pixelSize: 12
      lineHeight: 1.1
      color: WindowsTheme.textPrimary
    }
  }
}
