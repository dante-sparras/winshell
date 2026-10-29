import Winshell
import QtQuick
import "tasks.js" as Tasks

// The taskbar's apps: pinned apps, then running ones, each app's windows on one button. Hovering a button shows
// live previews of its windows, and a right click opens its menu.
Item {
  id: root

  // The open windows and the app index, as WindowList and Apps have them. Tests give stand-ins.
  property var windows: []
  property var activeWindow: null
  property var apps: null
  // The ids of the pinned apps, in order.
  property var pinned: []
  // The screen this taskbar is on, and whether it shows windows on other screens and the pinned apps.
  property var screen: null
  property bool allScreens: true
  property bool showPins: true
  // Whether a preview or a menu is open, so an auto-hiding taskbar stays up.
  readonly property bool busy: preview.visible || menu.visible
  readonly property alias preview: preview
  readonly property alias menu: menu

  readonly property var tasks: Tasks.tasks(windows, pinned, find, shows, showPins)

  signal pinRequested(string id)
  signal unpinRequested(string id)

  implicitWidth: row.implicitWidth
  implicitHeight: 40

  Accessible.role: Accessible.ToolBar
  Accessible.name: "Running applications"

  function find(name) {
    return apps && name ? apps.find(name) : null
  }

  function shows(window) {
    return allScreens || !screen || (window.screens || []).indexOf(screen) >= 0
  }

  function taskFor(key) {
    return (tasks || []).find(t => t.key === key) ?? null
  }

  // The window of each app used last, so a click on an app in the background brings back the one you had.
  property var lastUsed: ({})
  onActiveWindowChanged: if (activeWindow) lastUsed[Tasks.windowKey(activeWindow, find)] = activeWindow

  function launch(task) {
    if (task.entry)
      task.entry.launch()
    else if (task.windows.length > 0 && task.windows[0].executable)
      Shell.execDetached([task.windows[0].executable])
    else if (Tasks.isProgramPath(task.pin))
      Shell.execDetached([task.pin])
  }

  function invoke(task) {
    preview.hide()
    const action = Tasks.clickAction(task, lastUsed[task.key])
    if (action.launch)
      launch(task)
    else if (action.minimize)
      action.minimize.minimized = true
    else
      action.activate.activate()
  }

  Row {
    id: row
    anchors.verticalCenter: parent.verticalCenter
    spacing: 4

    // One button per app, kept by the app's key, so a window that comes or goes regroups the apps without
    // replacing the buttons a preview or a menu is anchored to.
    Repeater {
      model: root.tasks.map(t => t.key)

      TaskButton {
        id: button
        required property string modelData
        objectName: "task:" + modelData
        task: root.taskFor(modelData)
        onInvoked: root.invoke(task)
        onLaunchRequested: root.launch(task)
        onMenuRequested: {
          preview.hide()
          menu.open(button, modelData)
        }
        onContainsMouseChanged: {
          if (containsMouse && !menu.visible)
            preview.hoverStarted(button, modelData)
          else if (!containsMouse)
            preview.hoverEnded()
        }
        // The app is gone: what was open from its button closes with it.
        Component.onDestruction: {
          if (preview.anchor.item === button)
            preview.hide()
          if (menu.anchor.item === button)
            menu.visible = false
        }
      }
    }
  }

  TaskPreview {
    id: preview
    task: root.taskFor(taskKey)
    onActivateRequested: window => {
      hide()
      window.activate()
    }
  }

  TaskMenu {
    id: menu
    task: root.taskFor(taskKey)
    onLaunchRequested: root.launch(task)
    onPinRequested: root.pinRequested(task.pinId)
    onUnpinRequested: root.unpinRequested(task.pin)
  }
}
