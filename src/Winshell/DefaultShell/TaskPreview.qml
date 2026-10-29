import Winshell
import Winshell.Theme
import Winshell.Windows
import QtQuick
import "tasks.js" as Tasks

// What Windows 11 shows over a taskbar button you hover: a live preview of each of the app's windows with its
// title and a close button, or only the app's name when it is not running. A click on a preview brings that
// window forward.
PopupWindow {
  id: root

  property var task: null
  property string taskKey: ""

  signal activateRequested(var window)

  readonly property bool running: task !== null && task.windows.length > 0

  implicitWidth: content.implicitWidth
  implicitHeight: content.implicitHeight
  visible: false
  color: "transparent"
  WindowEffects.backdrop: WindowEffects.Acrylic
  WindowEffects.corners: WindowEffects.RoundCorners
  anchor.edges: Edges.Top
  anchor.gravity: Edges.Top
  anchor.margins.top: 8

  // A preview opens after the pointer rests on a button, moves at once to the next button, and closes a moment
  // after the pointer leaves both the button and the preview.
  function hoverStarted(button, key) {
    closeTimer.stop()
    anchor.item = button
    taskKey = key
    if (!visible)
      openTimer.restart()
  }

  function hoverEnded() {
    openTimer.stop()
    if (visible)
      closeTimer.restart()
  }

  function hide() {
    openTimer.stop()
    closeTimer.stop()
    visible = false
  }

  onTaskChanged: if (task === null) hide()

  Timer {
    id: openTimer
    interval: 400
    onTriggered: root.visible = root.task !== null
  }

  Timer {
    id: closeTimer
    interval: 300
    onTriggered: if (!hover.hovered) root.visible = false
  }

  Item {
    id: content
    implicitWidth: root.running ? cards.implicitWidth + 16 : name.implicitWidth + 24
    implicitHeight: root.running ? cards.implicitHeight + 16 : name.implicitHeight + 16

    HoverHandler {
      id: hover
      onHoveredChanged: hovered ? closeTimer.stop() : closeTimer.restart()
    }

    Text {
      id: name
      visible: !root.running
      anchors.centerIn: parent
      text: root.task ? Tasks.name(root.task) : ""
      font: Theme.typography.body
      color: Theme.textFillColorPrimary
    }

    Row {
      id: cards
      visible: root.running
      x: 8
      y: 8
      spacing: 4

      Repeater {
        model: root.running ? root.task.windows : []

        MouseArea {
          id: card
          required property var modelData
          objectName: "preview:" + modelData.title
          width: 216
          height: 160
          hoverEnabled: true
          Accessible.role: Accessible.Button
          Accessible.name: modelData.title
          Accessible.onPressAction: root.activateRequested(modelData)
          onClicked: root.activateRequested(modelData)

          Rectangle {
            anchors.fill: parent
            radius: Theme.controlCornerRadius
            color: card.pressed ? Theme.subtleFillColorTertiary : card.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
          }

          Image {
            id: icon
            x: 8
            y: 8
            width: 16
            height: 16
            sourceSize: Qt.size(16, 16)
            source: card.modelData.icon ? Shell.iconPath(card.modelData.icon) : ""
          }

          Text {
            anchors.left: icon.right
            anchors.leftMargin: 8
            anchors.right: close.left
            anchors.rightMargin: 4
            anchors.verticalCenter: icon.verticalCenter
            text: card.modelData.title
            elide: Text.ElideRight
            font: Theme.typography.caption
            color: Theme.textFillColorPrimary
          }

          MouseArea {
            id: close
            objectName: "previewClose"
            visible: card.containsMouse
            x: parent.width - width - 4
            y: 4
            width: 24
            height: 24
            hoverEnabled: true
            Accessible.role: Accessible.Button
            Accessible.name: "Close " + card.modelData.title
            Accessible.onPressAction: card.modelData.close()
            onClicked: card.modelData.close()

            Rectangle {
              anchors.fill: parent
              radius: Theme.controlCornerRadius
              color: close.pressed ? Theme.systemFillColorCritical : close.containsMouse ? Theme.systemFillColorCriticalBackground : "transparent"
            }

            Text {
              anchors.centerIn: parent
              text: ""
              font.family: Theme.iconFontFamily
              font.pixelSize: 10
              color: Theme.textFillColorPrimary
            }
          }

          WindowThumbnail {
            x: 8
            y: 32
            width: parent.width - 16
            height: parent.height - 40
            window: card.modelData
          }
        }
      }
    }
  }
}
