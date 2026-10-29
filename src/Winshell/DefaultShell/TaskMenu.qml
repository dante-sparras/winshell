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
  property int current: 0

  signal launchRequested()
  signal pinRequested()
  signal unpinRequested()

  readonly property var items: {
    if (!task)
      return []
    const count = task.windows.length
    const list = [{ id: "launch", label: Tasks.name(task), icon: task.icon }]
    list.push(task.pinned ? { id: "unpin", label: "Unpin from taskbar", glyph: "" } : { id: "pin", label: "Pin to taskbar", glyph: "" })
    if (count > 0)
      list.push({ id: "close", label: count === 1 ? "Close window" : "Close all windows", glyph: "" })
    return list
  }

  implicitWidth: 256
  implicitHeight: column.implicitHeight + 8
  visible: false
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: WindowEffects.Acrylic
  WindowEffects.corners: WindowEffects.RoundCorners
  anchor.edges: Edges.Top
  anchor.gravity: Edges.Top
  anchor.margins.top: 8

  function open(button, key) {
    anchor.item = button
    taskKey = key
    current = 0
    visible = true
    keys.forceActiveFocus()
  }

  function trigger(id) {
    const task = root.task
    visible = false
    if (!task)
      return
    if (id === "launch")
      launchRequested()
    else if (id === "pin")
      pinRequested()
    else if (id === "unpin")
      unpinRequested()
    else if (id === "close")
      task.windows.slice().forEach(w => w.close())
  }

  onTaskChanged: if (task === null) visible = false

  Item {
    id: keys
    anchors.fill: parent
    focus: true
    Accessible.role: Accessible.PopupMenu
    Accessible.name: root.task ? Tasks.name(root.task) : ""

    Keys.onPressed: event => {
      if (event.key === Qt.Key_Escape)
        root.visible = false
      else if (event.key === Qt.Key_Down)
        root.current = (root.current + 1) % root.items.length
      else if (event.key === Qt.Key_Up)
        root.current = (root.current + root.items.length - 1) % root.items.length
      else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space)
        root.trigger(root.items[root.current].id)
      else
        return
      event.accepted = true
    }

    Column {
      id: column
      x: 4
      y: 4
      width: parent.width - 8

      Repeater {
        model: root.items

        MouseArea {
          id: item
          required property var modelData
          required property int index
          objectName: "menu:" + modelData.id
          width: column.width
          height: 36
          hoverEnabled: true
          onContainsMouseChanged: if (containsMouse) root.current = index
          onClicked: root.trigger(modelData.id)
          Accessible.role: Accessible.MenuItem
          Accessible.name: modelData.label
          Accessible.onPressAction: root.trigger(modelData.id)

          Rectangle {
            anchors.fill: parent
            radius: Theme.controlCornerRadius
            color: item.pressed ? Theme.subtleFillColorTertiary : root.current === item.index ? Theme.subtleFillColorSecondary : "transparent"
          }

          Image {
            visible: item.modelData.icon !== undefined
            x: 12
            anchors.verticalCenter: parent.verticalCenter
            width: 16
            height: 16
            sourceSize: Qt.size(16, 16)
            source: item.modelData.icon ? Shell.iconPath(item.modelData.icon) : ""
          }

          Text {
            visible: item.modelData.glyph !== undefined
            x: 12
            anchors.verticalCenter: parent.verticalCenter
            width: 16
            horizontalAlignment: Text.AlignHCenter
            text: item.modelData.glyph ?? ""
            font.family: Theme.iconFontFamily
            font.pixelSize: 16
            color: Theme.textFillColorPrimary
          }

          Text {
            x: 40
            width: parent.width - 52
            anchors.verticalCenter: parent.verticalCenter
            text: item.modelData.label
            elide: Text.ElideRight
            font: Theme.typography.body
            color: Theme.textFillColorPrimary
          }
        }
      }
    }
  }
}
