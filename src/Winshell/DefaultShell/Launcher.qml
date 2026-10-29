import Winshell
import Winshell.Apps
import Winshell.Theme
import QtQuick
import "launcher.js" as Launcher

// Start. A bottom taskbar calls the same open, close, and toggle; the anchor flips when the bar sits on the bottom edge.
PopupWindow {
  id: launcher

  implicitWidth: Launcher.menuWidth
  implicitHeight: Launcher.menuHeight
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: Preferences.material
  WindowEffects.corners: WindowEffects.RoundCorners

  function open() {
    visible = true
  }

  function close() {
    visible = false
  }

  function toggle() {
    visible = !visible
  }

  onVisibleChanged: {
    if (visible)
      view.focusSearch()
    else
      view.reset()
  }

  LauncherView {
    id: view
    anchors.fill: parent
    apps: Apps.apps.values
    recent: Apps.recent.values
    showRecommended: Preferences.startRecommended
    searchResults: Apps.search(view.query)
    pinsPath: Shell.configPath("launcher.json")
    showIcons: launcher.visible
    onLaunched: app => {
      app.launch()
      launcher.close()
    }
    onLocationRequested: app => {
      Shell.execDetached(view.locationCommand(app))
      launcher.close()
    }
    onDismissed: launcher.close()
  }
}
