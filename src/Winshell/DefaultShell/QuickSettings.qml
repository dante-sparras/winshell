import Winshell
import QtQuick

// Quick settings, the popup window Windows 11 opens from the network, volume and battery icons, on Acrylic.
PopupWindow {
  id: root

  implicitWidth: content.implicitWidth
  implicitHeight: content.implicitHeight
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: WindowEffects.Acrylic
  WindowEffects.corners: WindowEffects.RoundCorners

  onVisibleChanged: if (visible) content.page = ""

  QuickSettingsContent {
    id: content
    anchors.fill: parent
    onCloseRequested: root.visible = false
  }
}
