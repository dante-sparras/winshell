import Winshell
import Winshell.Bluetooth
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

  // The Bluetooth singleton starts with the shell. Enumeration waits until this flyout is open.
  Component.onCompleted: if (!visible) Bluetooth.stopMonitoring()
  onVisibleChanged: {
    content.page = ""
    content.editing = false
    if (visible)
      Bluetooth.startMonitoring()
    else
      Bluetooth.stopMonitoring()
  }

  QuickSettingsContent {
    id: content
    anchors.fill: parent
    layoutPath: Shell.statePath("quick-settings.toml")
    onCloseRequested: root.visible = false
  }
}
