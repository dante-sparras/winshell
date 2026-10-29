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
  readonly property var windows: running ? task.windows : []
  // The work area of the taskbar's screen, and the room a preview has in it, with a margin on each side.
  property var workArea: null
  readonly property real roomWide: (workArea ? workArea.width : 1920) - 32
  readonly property real roomHigh: (workArea ? workArea.height : 1032) * 0.6
  // Previews shrink to fit the screen, keeping their shape. Below 152 px they would be too small to tell apart, so
  // the windows are listed by title instead, as Windows lists a large group.
  readonly property real cardWidth: Math.min(216, Math.floor((roomWide - 16 - Math.max(0, windows.length - 1) * 4) / Math.max(1, windows.length)))
  readonly property bool listed: cardWidth < 152

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
    implicitWidth: !root.running ? name.implicitWidth + 24 : root.listed ? list.width + 16 : cards.implicitWidth + 16
    implicitHeight: !root.running ? name.implicitHeight + 16 : root.listed ? list.height + 16 : cards.implicitHeight + 16

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
      visible: root.running && !root.listed
      x: 8
      y: 8
      spacing: 4

      Repeater {
        model: root.running && !root.listed ? root.windows : []

        MouseArea {
          id: card
          required property var modelData
          objectName: "preview:" + modelData.title
          width: root.cardWidth
          height: Math.round(root.cardWidth * 160 / 216)
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
            anchors.right: cardClose.left
            anchors.rightMargin: 4
            anchors.verticalCenter: icon.verticalCenter
            text: card.modelData.title
            elide: Text.ElideRight
            font: Theme.typography.caption
            color: Theme.textFillColorPrimary
          }

          CloseButton {
            id: cardClose
            window: card.modelData
            visible: card.containsMouse
            x: parent.width - width - 4
            y: 4
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

    // A group too large for previews: its windows by title, scrolling when they are more than the screen holds.
    Flickable {
      id: list
      objectName: "previewList"
      visible: root.running && root.listed
      x: 8
      y: 8
      // As wide as the screen allows, up to 320, so a close button is never past the popup's edge.
      width: Math.max(96, Math.min(320, root.roomWide - 16))
      height: Math.min(contentHeight, root.roomHigh)
      contentWidth: width
      contentHeight: rows.implicitHeight
      flickableDirection: Flickable.VerticalFlick
      boundsBehavior: Flickable.StopAtBounds
      interactive: contentHeight > height
      clip: true

      Column {
        id: rows
        width: list.width

        Repeater {
          model: root.running && root.listed ? root.windows : []

          MouseArea {
            id: line
            required property var modelData
            objectName: "previewRow:" + modelData.title
            width: rows.width
            height: 40
            hoverEnabled: true
            Accessible.role: Accessible.Button
            Accessible.name: modelData.title
            Accessible.onPressAction: root.activateRequested(modelData)
            onClicked: root.activateRequested(modelData)

            Rectangle {
              anchors.fill: parent
              radius: Theme.controlCornerRadius
              color: line.pressed ? Theme.subtleFillColorTertiary : line.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
            }

            Image {
              id: lineIcon
              x: 12
              anchors.verticalCenter: parent.verticalCenter
              width: 16
              height: 16
              sourceSize: Qt.size(16, 16)
              source: line.modelData.icon ? Shell.iconPath(line.modelData.icon) : ""
            }

            Text {
              anchors.left: lineIcon.right
              anchors.leftMargin: 12
              anchors.right: lineClose.left
              anchors.rightMargin: 4
              anchors.verticalCenter: parent.verticalCenter
              text: line.modelData.title
              elide: Text.ElideRight
              font: Theme.typography.body
              color: Theme.textFillColorPrimary
            }

            CloseButton {
              id: lineClose
              window: line.modelData
              visible: line.containsMouse
              x: parent.width - width - 8
              anchors.verticalCenter: parent.verticalCenter
            }
          }
        }
      }
    }
  }

  component CloseButton: MouseArea {
    id: close
    property var window
    objectName: "previewClose"
    width: 24
    height: 24
    hoverEnabled: true
    Accessible.role: Accessible.Button
    Accessible.name: "Close " + (window ? window.title : "")
    Accessible.onPressAction: window.close()
    onClicked: window.close()

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
}
