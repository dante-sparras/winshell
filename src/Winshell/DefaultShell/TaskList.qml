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
  // The button the keyboard was on has gone. The taskbar focuses one that is still there, never this one.
  signal keyboardDisplaced()

  // The most room the apps may take. When they need more, they scroll sideways with the mouse wheel or a drag.
  property real maxWidth: Infinity
  // How wide all the apps are side by side, before any limit.
  readonly property real naturalWidth: row.implicitWidth

  implicitWidth: Math.min(row.implicitWidth, Math.max(0, maxWidth))
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

  // The button with keyboard focus is scrolled fully into view, as Tab, the arrows or Narrator reach it.
  function reveal(button) {
    if (button.x < strip.contentX)
      strip.contentX = button.x
    else if (button.x + button.width > strip.contentX + strip.width)
      strip.contentX = button.x + button.width - strip.width
  }

  Flickable {
    id: strip
    // A narrower strip keeps the focused button in view.
    onWidthChanged: {
      for (let i = 0; i < row.children.length; i++) {
        const child = row.children[i]
        if (child.activeFocus)
          root.reveal(child)
      }
    }
    objectName: "taskStrip"
    anchors.fill: parent
    contentWidth: row.implicitWidth
    contentHeight: height
    flickableDirection: Flickable.HorizontalFlick
    boundsBehavior: Flickable.StopAtBounds
    interactive: contentWidth > width
    clip: true

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
          onPreviewRequested: preview.openFromKeyboard(button, modelData)
          onActiveFocusChanged: if (activeFocus) root.reveal(button)
          // Pinning or reordering keeps this button and moves it. Focus does not change, so the strip would
          // otherwise stay scrolled to where the button used to be.
          onXChanged: if (activeFocus) Qt.callLater(() => { if (button.activeFocus) root.reveal(button) })
          onContainsMouseChanged: {
            if (containsMouse && !menu.visible)
              preview.hoverStarted(button, modelData)
            else if (!containsMouse)
              preview.hoverEnded()
          }
          // The app is gone: what was open from its button closes with it. Read before hide(): closing drops
          // the keyboard flag, and the button is not a place focus can return to.
          Component.onDestruction: {
            const keyboard = button.activeFocus
              || (preview.grabFocus && preview.anchor.item === button)
              || (menu.visible && menu.anchor.item === button)
            if (preview.anchor.item === button)
              preview.hide()
            if (menu.anchor.item === button)
              menu.visible = false
            if (keyboard)
              Qt.callLater(() => root.keyboardDisplaced())
          }
        }
      }
    }
  }

  TaskPreview {
    id: preview
    task: root.taskFor(taskKey)
    workArea: root.screen ? root.screen.workArea : null
    onActivateRequested: window => {
      hide()
      window.activate()
    }
    onDismissed: if (anchor.item) anchor.item.forceActiveFocus(Qt.TabFocusReason)
    onAnchorGone: Qt.callLater(() => root.keyboardDisplaced())
  }

  TaskMenu {
    id: menu
    task: root.taskFor(taskKey)
    onLaunchRequested: root.launch(task)
    onPinRequested: root.pinRequested(task.pinId)
    onUnpinRequested: root.unpinRequested(task.pin)
    onDismissed: if (anchor.item) anchor.item.forceActiveFocus(Qt.TabFocusReason)
    onAnchorGone: Qt.callLater(() => root.keyboardDisplaced())
  }
}
