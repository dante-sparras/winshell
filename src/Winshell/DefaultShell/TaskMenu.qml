import Winshell
import Winshell.Theme
import QtQuick
import "tasks.js" as Tasks

// The menu Windows 11 opens from a right click on a taskbar button, the part of a jump list every app has: open
// the app, pin or unpin it, and close its windows. The arrow keys move between the items, and Escape closes it.
PopupWindow {
  id: root

  property var task: null
  property string taskKey: ""

  signal launchRequested()
  signal pinRequested()
  signal unpinRequested()
  // Escape closed the menu, so the keyboard goes back to the button it opened from.
  signal dismissed()

  readonly property var items: {
    if (!task)
      return []
    const count = task.windows.length
    const list = [{ id: "launch", label: Tasks.name(task), icon: task.icon }]
    // One slot for pinning, whichever way it goes, so its row and its focus stay while the app is pinned or unpinned.
    list.push({ id: "pin", label: task.pinned ? "Unpin from taskbar" : "Pin to taskbar", glyph: task.pinned ? "" : "" })
    if (count > 0)
      list.push({ id: "close", label: count === 1 ? "Close window" : "Close all windows", glyph: "" })
    return list
  }

  implicitWidth: 256
  implicitHeight: column.implicitHeight + 8
  visible: false
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: Preferences.material
  WindowEffects.corners: WindowEffects.RoundCorners
  anchor.edges: Edges.Top
  anchor.gravity: Edges.Top
  anchor.margins.top: 8

  // The first item takes focus, so Narrator reads it and the arrow keys move from it.
  function open(button, key) {
    anchor.item = button
    taskKey = key
    visible = true
    if (rows.count > 0)
      rows.itemAt(0).forceActiveFocus(Qt.ShortcutFocusReason)
  }

  function itemFor(id) {
    return items.find(i => i.id === id) ?? null
  }

  function trigger(id) {
    const task = root.task
    visible = false
    if (!task)
      return
    if (id === "launch")
      launchRequested()
    else if (id === "pin" && task.pinned)
      unpinRequested()
    else if (id === "pin")
      pinRequested()
    else if (id === "close")
      task.windows.slice().forEach(w => w.close())
  }

  onTaskChanged: if (task === null) visible = false

  // An item that went away, as Close when the app's last window closes, took focus with it: the first item gets it.
  onItemsChanged: if (visible) Qt.callLater(() => {
    if (visible && activeFocusItem === null && rows.count > 0)
      rows.itemAt(0).forceActiveFocus(Qt.TabFocusReason)
  })

  Item {
    id: keys
    anchors.fill: parent
    Accessible.role: Accessible.PopupMenu
    Accessible.name: root.task ? Tasks.name(root.task) : ""

    // Focus itself moves between the items, wrapping around, so Narrator announces each one.
    Keys.onPressed: event => {
      event.accepted = false
      const focused = root.activeFocusItem
      if (event.key === Qt.Key_Escape) {
        root.visible = false
        root.dismissed()
      } else if ((event.key === Qt.Key_Down || event.key === Qt.Key_Up) && focused) {
        const next = focused.nextItemInFocusChain(event.key === Qt.Key_Down)
        if (next)
          next.forceActiveFocus(Qt.TabFocusReason)
      } else {
        return
      }
      event.accepted = true
    }

    Column {
      id: column
      x: 4
      y: 4
      width: parent.width - 8

      // Rows are kept by their slot, so the app's windows coming and going doesn't replace the focused row.
      Repeater {
        id: rows
        model: root.items.map(i => i.id)

        MouseArea {
          id: item
          required property string modelData
          readonly property var entry: root.itemFor(modelData) ?? { id: modelData, label: "" }
          objectName: "menu:" + modelData
          width: column.width
          height: 36
          hoverEnabled: true
          activeFocusOnTab: true
          // The pointer and the keyboard share one current item: the one with focus.
          onContainsMouseChanged: if (containsMouse) forceActiveFocus()
          onClicked: root.trigger(modelData)
          Keys.onPressed: event => {
            event.accepted = false
            if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
              root.trigger(modelData)
              event.accepted = true
            }
          }
          Accessible.role: Accessible.MenuItem
          Accessible.name: entry.label
          Accessible.onPressAction: root.trigger(modelData)

          Rectangle {
            anchors.fill: parent
            radius: Theme.controlCornerRadius
            color: item.pressed ? Theme.subtleFillColorTertiary : item.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
          }

          Image {
            visible: item.entry.icon !== undefined
            x: 12
            anchors.verticalCenter: parent.verticalCenter
            width: 16
            height: 16
            sourceSize: Qt.size(16, 16)
            source: item.entry.icon ? Shell.iconPath(item.entry.icon) : ""
          }

          Text {
            visible: item.entry.glyph !== undefined
            x: 12
            anchors.verticalCenter: parent.verticalCenter
            width: 16
            horizontalAlignment: Text.AlignHCenter
            text: item.entry.glyph ?? ""
            font.family: Theme.iconFontFamily
            font.pixelSize: 16
            color: Theme.textFillColorPrimary
          }

          Text {
            x: 40
            width: parent.width - 52
            anchors.verticalCenter: parent.verticalCenter
            text: item.entry.label
            elide: Text.ElideRight
            font: Theme.typography.body
            color: Theme.textFillColorPrimary
          }
        }
      }
    }
  }
}
