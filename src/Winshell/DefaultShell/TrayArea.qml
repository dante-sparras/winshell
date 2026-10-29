import Winshell
import Winshell.Theme
import Winshell.Tray
import QtQuick

// The taskbar's default tray area, as Windows 11 lays it out: the chevron that opens the hidden icons, the icons
// Settings shows on the taskbar, and the network, volume and battery icons that open quick settings. While a Winshell
// update waits for a restart, an icon for it opens its card in Winshell Settings, as Windows Update's does.
Row {
  id: root

  // The taskbar window, for the apps' menus.
  property var window: null
  // Whether a flyout of this area is open, so an auto-hiding taskbar stays up.
  readonly property bool busy: overflow.visible || quickSettings.visible

  readonly property var items: SystemTray.items.values

  height: 40
  spacing: 0

  TaskbarButton {
    id: chevron
    objectName: "trayChevron"
    visible: root.items.some(i => i.overflow)
    implicitWidth: 24
    glyph: overflow.visible ? "" : ""
    label: "Show hidden icons"
    checked: overflow.visible
    onInvoked: overflow.visible = !overflow.visible
  }

  Repeater {
    model: root.items.filter(i => !i.overflow)

    TrayIcon {
      required property var modelData
      item: modelData
      window: root.window
    }
  }

  TaskbarButton {
    objectName: "updateReady"
    visible: Updates.status === UpdateStatus.Ready
    implicitWidth: 32
    glyph: ""
    label: "Winshell " + Updates.version + " is ready. Restart Winshell to update"
    onInvoked: if (root.window) root.window.updatesRequested()
  }

  QuickSettingsButton {
    id: quickSettingsButton
    anchors.verticalCenter: parent.verticalCenter
    onClicked: quickSettings.visible = !quickSettings.visible
  }

  PopupWindow {
    id: overflow
    visible: false
    implicitWidth: grid.implicitWidth + 8
    implicitHeight: grid.implicitHeight + 8
    color: "transparent"
    grabFocus: true
    WindowEffects.backdrop: Preferences.material
    WindowEffects.corners: WindowEffects.RoundCorners
    anchor.item: chevron
    anchor.edges: Edges.Top
    anchor.gravity: Edges.Top
    anchor.margins.top: 8

    Grid {
      id: grid
      x: 4
      y: 4
      columns: 5
      Accessible.role: Accessible.List
      Accessible.name: "Hidden icons"

      Repeater {
        model: root.items.filter(i => i.overflow)

        TrayIcon {
          required property var modelData
          implicitWidth: 40
          item: modelData
          window: overflow
        }
      }
    }
  }

  QuickSettings {
    id: quickSettings
    anchor.item: quickSettingsButton
    anchor.edges: Edges.Top | Edges.Right
    anchor.gravity: Edges.Top | Edges.Left
    anchor.margins.top: 8
  }
}
