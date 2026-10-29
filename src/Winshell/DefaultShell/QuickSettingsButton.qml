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
        required property int index
        anchors.verticalCenter: parent.verticalCenter
        text: modelData
        font.family: Theme.iconFontFamily
        font.pixelSize: 16
        color: Theme.textFillColorPrimary

        // The Wi-Fi bars the signal does not reach, under the ones it does.
        Text {
          objectName: "wifiTrack"
          z: -1
          visible: index === 0 && text !== ""
          text: SystemStatus.networkTrackGlyph
          font.family: Theme.iconFontFamily
          font.pixelSize: 16
          color: Theme.textFillColorDisabled
        }
      }
    }
  }
}
