import Winshell
import Winshell.Theme
import QtQuick

// The menu Windows 11 opens from a right click on an empty part of the taskbar: Taskbar settings.
PopupWindow {
  id: root

  signal settingsRequested()

  implicitWidth: 256
  implicitHeight: 44
  visible: false
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: Preferences.material
  WindowEffects.corners: WindowEffects.RoundCorners
  anchor.edges: Edges.Top
  anchor.gravity: Edges.Top

  // Opens at a point along the top of the taskbar.
  function openAt(x) {
    anchor.rect.x = x
    anchor.rect.y = 0
    visible = true
    item.forceActiveFocus()
  }

  function trigger() {
    visible = false
    settingsRequested()
  }

  // Narrator reads the item inside a menu, as it reads Windows' own.
  Item {
    anchors.fill: parent
    Accessible.role: Accessible.PopupMenu
    Accessible.name: "Taskbar"

    SubtleButton {
      id: item
      objectName: "menu:taskbarSettings"
      x: 4
      y: 4
      width: parent.width - 8
      height: 36
      focus: true
      hoverEnabled: true
      onActivated: root.trigger()
      Accessible.role: Accessible.MenuItem
      Accessible.name: "Taskbar settings"
      Keys.onEscapePressed: root.visible = false

      Rectangle {
        anchors.fill: parent
        radius: Theme.controlCornerRadius
        color: item.pressed ? Theme.subtleFillColorTertiary : item.containsMouse || item.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
      }

      Text {
        x: 12
        anchors.verticalCenter: parent.verticalCenter
        text: "\uE713"
        font.family: Theme.iconFontFamily
        font.pixelSize: 16
        color: Theme.textFillColorPrimary
        Accessible.ignored: true
      }

      Text {
        x: 40
        anchors.verticalCenter: parent.verticalCenter
        text: "Taskbar settings"
        font: Theme.typography.body
        color: Theme.textFillColorPrimary
      }
    }
  }
}
