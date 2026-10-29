import Winshell
import Winshell.Apps
import Winshell.Io
import QtQuick
import "settings.js" as Settings

// Winshell Settings: a normal window on Mica, which the taskbar's right-click menu, a hotkey and
// `winshell ipc call settings open <page>` open. Closing it hides it; the next open brings it back as it was.
FloatingWindow {
  id: root

  property alias notificationListener: view.notificationListener
  property alias startHotkey: view.startHotkey
  property alias settingsHotkey: view.settingsHotkey

  title: "Winshell Settings"
  implicitWidth: Settings.windowWidth
  implicitHeight: Settings.windowHeight
  minimumSize: Qt.size(680, 480)
  visible: false
  color: "transparent"
  WindowEffects.backdrop: WindowEffects.Mica

  // Opens a page, or the page of a setting with the keyboard on it; "" keeps the page it was on.
  function open(key) {
    if (key)
      view.open(key)
    activate()
    if (!key)
      view.focusSearch()
  }

  SettingsView {
    id: view
    anchors.fill: parent
    apps: Apps.apps.values
    pinsPath: Shell.configPath("launcher.json")
    tilesPath: Shell.statePath("quick-settings.json")
    showIcons: root.visible
  }

  IpcHandler {
    target: "settings"

    function open(page: string): void {
      root.open(page)
    }
  }
}
