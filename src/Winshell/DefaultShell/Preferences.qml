pragma Singleton
import Winshell
import Winshell.Io
import Winshell.Theme
import QtQuick

// The choices made in Winshell Settings, kept in preferences.toml. A config in the config folder (%APPDATA%\winshell,
// or WINSHELL_CONFIG_DIR) keeps the file beside its shell.qml, so two configs running at once each have their own
// hotkeys. The default shell, which lives outside that folder, keeps it in the config folder itself. Every surface
// reads them here and follows a change at once, including one made to the file by hand. A key the file does not have
// keeps its default when Winshell starts. Surfaces read the values below that fall back to the default when the file
// has one Winshell cannot use.
Singleton {
  id: root

  // Without a config folder, as in tests, the choices last for this run only.
  property string path: folder ? folder + "/preferences.toml" : ""
  // The folder that holds the file, which Winshell Settings opens.
  readonly property string folder: {
    if (!Shell.configRoot)
      return ""
    const key = dir => dir.replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase()
    const shell = key(Shell.shellDir)
    const config = key(Shell.configRoot)
    return shell === config || shell.startsWith(config + "/") ? Shell.shellDir : Shell.configRoot
  }

  // "system", "light" or "dark".
  property alias colorScheme: adapter.colorScheme
  // "" for the accent picked in Windows, or "#rrggbb".
  property alias accentColor: adapter.accentColor
  // "acrylic" or "mica": what shows through the taskbar and the flyouts.
  property alias backdrop: adapter.backdrop

  property alias taskbarCentered: adapter.taskbarCentered
  property alias taskbarAutoHide: adapter.taskbarAutoHide
  // false shows a taskbar on the main screen only.
  property alias taskbarAllScreens: adapter.taskbarAllScreens
  // "all", "main" or "where", as Taskbar.appsOn takes it.
  property alias taskbarAppsOn: adapter.taskbarAppsOn

  property alias startRecommended: adapter.startRecommended

  // The apps whose notifications the notification center leaves out, as { id, name }.
  property alias hiddenNotificationApps: adapter.hiddenNotificationApps

  // Hotkey sequences; "" turns one off.
  property alias startHotkey: adapter.startHotkey
  property alias settingsHotkey: adapter.settingsHotkey

  // Check for, download and install Winshell updates by itself. Only a portable Winshell updates itself.
  property alias autoUpdate: adapter.autoUpdate

  readonly property int material: backdrop === "mica" ? WindowEffects.Mica : WindowEffects.Acrylic
  readonly property bool validAccent: /^#[0-9a-fA-F]{6}$/.test(accentColor)
  readonly property string appsOn: ["all", "main", "where"].includes(taskbarAppsOn) ? taskbarAppsOn : "all"
  // The hidden apps that have an id.
  readonly property var hiddenApps: Array.isArray(hiddenNotificationApps)
    ? hiddenNotificationApps.filter(app => app !== null && typeof app === "object" && typeof app.id === "string")
    : []

  function showsNotificationsFrom(appId) {
    return !hiddenApps.some(app => app.id === appId)
  }

  // Writes the file if there is none yet, which makes the config folder, so the About page can open it.
  function save() {
    if (path && !file.loaded)
      file.writeAdapter()
  }

  function setNotificationsFrom(appId, appName, shown) {
    const rest = hiddenApps.filter(app => app.id !== appId)
    hiddenNotificationApps = shown ? rest : rest.concat([{ id: appId, name: appName }])
  }

  Binding {
    target: Updates
    property: "automatic"
    value: root.autoUpdate
  }

  Binding {
    target: Theme
    property: "colorScheme"
    value: ["system", "light", "dark"].includes(root.colorScheme) ? root.colorScheme : "system"
  }

  Binding {
    target: Theme
    property: "accentColor"
    value: root.validAccent ? root.accentColor : "transparent"
  }

  FileView {
    id: file
    path: root.path
    blockLoading: true
    blockWrites: true
    atomicWrites: true
    watchChanges: true
    printErrors: false
    onFileChanged: reload()
    onAdapterUpdated: if (root.path) writeAdapter()

    TomlAdapter {
      id: adapter
      // Written above a key the file does not have yet, so the file explains itself.
      comments: ({
        colorScheme: "\"system\" follows Windows; or \"light\" or \"dark\".",
        accentColor: "\"\" for the accent color picked in Windows, or \"#rrggbb\".",
        backdrop: "What shows through the taskbar and the flyouts: \"acrylic\" or \"mica\".",
        taskbarCentered: "true centers the taskbar's apps; false puts them on the left.",
        taskbarAutoHide: "true hides the taskbar until the pointer reaches it.",
        taskbarAllScreens: "true shows a taskbar on every display; false on the main display only.",
        taskbarAppsOn: "With several displays: \"all\" shows every app on every taskbar, \"main\" every app on the main\ntaskbar and each display's own windows on the others, \"where\" each window on its display's taskbar only.",
        startRecommended: "true shows recommended apps in Start under the pins.",
        hiddenNotificationApps: "The apps the notification center leaves out, as { id = \"<AppUserModelID>\", name = \"<app name>\" }.",
        startHotkey: "The hotkey that opens Start, such as \"Win\" or \"Ctrl+Alt+S\"; \"\" turns it off.",
        settingsHotkey: "The hotkey that opens Winshell Settings; \"\" turns it off.",
        autoUpdate: "true checks for, downloads and installs Winshell updates. Only a portable Winshell updates itself."
      })
      property string colorScheme: "system"
      property string accentColor: ""
      property string backdrop: "acrylic"
      property bool taskbarCentered: true
      property bool taskbarAutoHide: false
      property bool taskbarAllScreens: true
      property string taskbarAppsOn: "all"
      property bool startRecommended: true
      property var hiddenNotificationApps: []
      property string startHotkey: "Win"
      property string settingsHotkey: ""
      property bool autoUpdate: true
    }
  }
}
