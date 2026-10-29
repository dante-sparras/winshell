import Winshell
import QtQuick

// Quick settings, the popup window Windows 11 opens from the network, volume and battery icons, on Acrylic.
PopupWindow {
  id: root

  implicitWidth: content.implicitWidth
  implicitHeight: content.implicitHeight
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: Preferences.material
  WindowEffects.corners: WindowEffects.RoundCorners

  onVisibleChanged: {
    content.page = ""
    content.editing = false
  }

  QuickSettingsContent {
    id: content
    anchors.fill: parent
    layoutPath: Shell.statePath("quick-settings.json")
    onCloseRequested: root.visible = false
  }
}
