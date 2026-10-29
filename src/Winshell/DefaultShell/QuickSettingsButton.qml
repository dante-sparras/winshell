import Winshell.Theme
import QtQuick
import QtQuick.Controls

// The network, volume and battery icons that open quick settings, as on the Windows 11 taskbar.
Button {
  id: root

  implicitHeight: 32
  leftPadding: 10
  rightPadding: 10
  flat: true
  text: "Quick settings"

  contentItem: Row {
    spacing: 10

    Repeater {
      model: [SystemStatus.networkGlyph, SystemStatus.volumeGlyph].concat(SystemStatus.hasBattery ? [SystemStatus.batteryGlyph] : [])

      Text {
        required property string modelData
        anchors.verticalCenter: parent.verticalCenter
        text: modelData
        font.family: Theme.iconFontFamily
        font.pixelSize: 16
        color: Theme.textFillColorPrimary
      }
    }
  }
}
