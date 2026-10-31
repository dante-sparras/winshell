import Winshell
import Winshell.Theme
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
  // Dragging one pinned app onto another. The ids are the ones TaskbarPins keeps.
  signal moveRequested(string id, string target)
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

  // The window of each app used last, so a click on an app in the background brings back the one you had. A window
  // that closes is dropped, so the map never keeps it.
  property var lastUsed: ({})
  onActiveWindowChanged: if (activeWindow) lastUsed[Tasks.windowKey(activeWindow, find)] = activeWindow
  onWindowsChanged: {
    for (const key of Object.keys(lastUsed)) {
      if (windows.indexOf(lastUsed[key]) < 0)
        delete lastUsed[key]
    }
  }

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

  // Win+number: the app at that place, counted from 0, as a click on its button. newWindow opens another window of
  // it, as Win+Shift+number does.
  function invokeAt(index, newWindow) {
    const task = (tasks || [])[index]
    if (!task)
      return
    if (newWindow)
      launch(task)
    else
      invoke(task)
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
      // The pinned button being dragged and the one it would land on, as indexes into the buttons, or -1.
      // Nothing is drawn from these until the pointer has moved onto another pin.
      property int dragIndex: -1
      property int dropIndex: -1
      property string dragName: ""
      // That release reorders, so it must not also be the button's click.
      property bool suppressClick: false

      function buttons() {
        return children.filter(c => c.objectName && c.objectName.startsWith("task:"))
      }

      function buttonAt(i) {
        const items = buttons()
        return i >= 0 && i < items.length ? items[i] : null
      }

      // Where this button sits. Read from the row, because a delegate's index stays at the place it was made.
      function indexOf(button) {
        const items = buttons()
        for (let i = 0; i < items.length; i++) {
          if (items[i].objectName === button.objectName)
            return i
        }
        return -1
      }

      // The pin under a point in this row. Pins are the buttons at the start, each the same width.
      function indexAt(x) {
        const button = buttonAt(0)
        if (!button)
          return -1
        let pinnedCount = 0
        for (let i = 0; i < root.tasks.length; i++) {
          if (root.tasks[i].pinned)
            pinnedCount++
        }
        if (pinnedCount < 1)
          return -1
        const at = Math.floor((x + spacing / 2) / (button.width + spacing))
        return Math.max(0, Math.min(pinnedCount - 1, at))
      }

      // One button per app, kept by the app's key, so a window that comes or goes regroups the apps without
      // replacing the buttons a preview or a menu is anchored to.
      Repeater {
        model: root.tasks.map(t => t.key)

        TaskButton {
          id: button
          required property string modelData
          objectName: "task:" + modelData
          task: root.taskFor(modelData)
          opacity: row.dragName === objectName && row.dropIndex !== row.dragIndex ? 0.5 : 1
          // A drag that lands on another pin is not also a click. A press that stays put still is.
          onInvoked: if (!row.suppressClick) root.invoke(task)
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
            if (row.dragIndex >= 0 && row.dropIndex !== row.dragIndex)
              return
            if (containsMouse && !menu.visible)
              preview.hoverStarted(button, modelData)
            else if (!containsMouse)
              preview.hoverEnded()
          }
          onPressed: mouse => {
            if (!task || !task.pinned || mouse.button !== Qt.LeftButton || (mouse.modifiers & Qt.ShiftModifier))
              return
            // The strip scrolls by dragging too. A pin keeps the press, or the scroll would take the reorder.
            button.preventStealing = true
            row.dragName = button.objectName
            row.dragIndex = row.dropIndex = row.indexOf(button)
          }
          onPositionChanged: mouse => {
            if (row.dragName !== button.objectName)
              return
            const p = button.mapToItem(row, mouse.x, mouse.y)
            const at = row.indexAt(p.x)
            if (at === row.dropIndex)
              return
            row.dropIndex = at
            if (at !== row.dragIndex)
              preview.hide()
          }
          onReleased: {
            button.preventStealing = false
            if (row.dragName !== button.objectName)
              return
            const from = row.dragIndex
            const to = row.dropIndex
            const source = root.tasks[from]
            const target = root.tasks[to]
            const sourceId = source ? source.pin : ""
            const targetId = target ? target.pin : ""
            row.dragName = ""
            row.dragIndex = row.dropIndex = -1
            if (from === to || !sourceId || !targetId)
              return
            row.suppressClick = true
            // The buttons are made again for the new order, so not while this one handles its release.
            Qt.callLater(() => {
              row.suppressClick = false
              root.moveRequested(sourceId, targetId)
            })
          }
          onCanceled: {
            button.preventStealing = false
            if (row.dragName === button.objectName) {
              row.dragName = ""
              row.dragIndex = row.dropIndex = -1
            }
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

  // Where the dragged pin would land. Hidden until a drag has moved onto another pin, so a resting taskbar
  // draws the same as before.
  Rectangle {
    objectName: "taskDrop"
    visible: row.dragIndex >= 0 && row.dropIndex >= 0 && row.dropIndex !== row.dragIndex
    width: 2
    radius: 1
    color: Theme.accentFillColorDefault
    height: {
      const mark = row.buttonAt(row.dropIndex)
      return mark && visible ? mark.height : 0
    }
    x: {
      const mark = row.buttonAt(row.dropIndex)
      if (!mark)
        return 0
      const before = row.dropIndex < row.dragIndex
      return mark.mapToItem(root, before ? -7 : mark.width + 5, 0).x
    }
    y: {
      const mark = row.buttonAt(row.dropIndex)
      return mark ? mark.mapToItem(root, 0, 0).y : 0
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
