import Winshell.Theme
import QtQuick

// A button on the taskbar that is not an app, such as Start or Search: a glyph, or items of its own, on a subtle
// fill when hovered, pressed, or while what it opens is open.
SubtleButton {
  id: root

  property string glyph
  property string label
  property bool checked
  // Draws the Windows logo, for Start, instead of a glyph.
  property bool windowsLogo

  signal invoked()

  implicitWidth: 40
  implicitHeight: 40
  hoverEnabled: true
  activeFocusOnTab: true
  onActivated: root.invoked()
  Accessible.name: label

  Rectangle {
    anchors.fill: parent
    radius: Theme.controlCornerRadius
    color: root.pressed ? Theme.subtleFillColorTertiary : root.containsMouse || root.checked ? Theme.subtleFillColorSecondary : "transparent"
    border.width: root.checked ? 1 : 0
    border.color: Theme.controlStrokeColorDefault

    Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
  }

  Item {
    anchors.fill: parent
    scale: root.pressed ? 0.85 : 1

    Behavior on scale { NumberAnimation { duration: Theme.controlFasterAnimationDuration } }

    Text {
      visible: root.glyph !== ""
      anchors.centerIn: parent
      text: root.glyph
      font.family: Theme.iconFontFamily
      font.pixelSize: 16
      color: Theme.textFillColorPrimary
    }

    // Four squares in Windows blue.
    Grid {
      visible: root.windowsLogo
      anchors.centerIn: parent
      columns: 2
      spacing: 1

      Repeater {
        model: 4

        Rectangle {
          width: 9
          height: 9
          radius: 1
          color: Theme.highContrast ? Theme.accentFillColorDefault : Theme.dark ? "#4cc2ff" : "#0078d4"
        }
      }
    }
  }
}
