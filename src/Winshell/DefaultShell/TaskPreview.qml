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
  // Escape closed previews opened from the keyboard, or their last window closed, so the keyboard goes back to
  // their button.
  signal dismissed()
  // The app these previews were for is gone, and the keyboard was in them. Their button is going too, so the
  // taskbar has to choose another place for it.
  signal anchorGone()

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
  WindowEffects.backdrop: Preferences.material
  WindowEffects.corners: WindowEffects.RoundCorners
  anchor.edges: Edges.Top
  anchor.gravity: Edges.Top
  anchor.margins.top: 8

  // A preview opens after the pointer rests on a button, moves at once to the next button, and closes a moment
  // after the pointer leaves both the button and the preview.
  function hoverStarted(button, key) {
    // The keyboard holds these previews. Crossing another button must not move them onto that app, or
    // Escape would return to a button that was never chosen.
    if (visible && grabFocus)
      return
    closeTimer.stop()
    anchor.item = button
    taskKey = key
    if (!visible)
      openTimer.restart()
  }

  function hoverEnded() {
    openTimer.stop()
    // The keyboard has these previews. The pointer leaving must not close them, or the focus they hold goes with
    // them and never comes back to a button.
    if (visible && !grabFocus)
      closeTimer.restart()
  }

  function hide() {
    openTimer.stop()
    closeTimer.stop()
    visible = false
  }

  // From the keyboard: the previews open at once and take the keyboard, with the first one focused.
  function openFromKeyboard(button, key) {
    openTimer.stop()
    closeTimer.stop()
    anchor.item = button
    taskKey = key
    if (task === null)
      return
    grabFocus = true
    visible = true
    focusWindow(null)
  }

  // The window the keyboard was on, so it stays on it when a group switches between previews and its list.
  property var focusedWindow: null

  // Focus goes to this window's preview or row, or else the first one.
  function focusWindow(window) {
    const repeater = listed ? lines : previews
    for (let i = 0; i < repeater.count; i++) {
      const item = repeater.itemAt(i)
      if (item && item.modelData === window) {
        item.forceActiveFocus(Qt.TabFocusReason)
        return
      }
    }
    if (repeater.count > 0)
      repeater.itemAt(0).forceActiveFocus(Qt.TabFocusReason)
  }

  // The window a pointer rests on the preview of. Windows peeks at it, if the user has peek on.
  property var peekWindow: null

  function pointerOn(window) {
    peekTimer.stop()
    peekTimer.window = window
    peekWindow = null
    if (window)
      peekTimer.restart()
  }

  // Opened by hovering again, the previews leave the keyboard where it is.
  onVisibleChanged: if (!visible) {
    grabFocus = false
    pointerOn(null)
  }
  onGrabFocusChanged: if (grabFocus) closeTimer.stop()

  onTaskChanged: if (task === null) {
    // Captured before hide() drops it. The windows-change handler runs too late: the button is already gone.
    const keyboard = grabFocus
    hide()
    if (keyboard)
      anchorGone()
  }

  // A window that closed, or a group that switched between previews and its list, took focus with it: it goes to
  // the same window if it is still there, or the first one; with no windows left, back to the app's button.
  onWindowsChanged: if (grabFocus) Qt.callLater(() => {
    if (!visible || !grabFocus || activeFocusItem !== null)
      return
    if (windows.length === 0) {
      hide()
      dismissed()
    } else {
      focusWindow(focusedWindow)
    }
  })
  onListedChanged: if (grabFocus) Qt.callLater(() => {
    if (visible && grabFocus)
      focusWindow(focusedWindow)
  })

  Timer {
    id: openTimer
    interval: 400
    onTriggered: root.visible = root.task !== null
  }

  Timer {
    id: peekTimer
    property var window: null
    interval: 400
    onTriggered: root.peekWindow = window
  }

  Timer {
    id: closeTimer
    interval: 300
    onTriggered: if (!hover.hovered && !root.grabFocus) root.visible = false
  }

  Item {
    id: content
    implicitWidth: !root.running ? name.implicitWidth + 24 : root.listed ? list.width + 16 : cards.implicitWidth + 16
    implicitHeight: !root.running ? name.implicitHeight + 16 : root.listed ? list.height + 16 : cards.implicitHeight + 16

    HoverHandler {
      id: hover
      onHoveredChanged: {
        if (root.grabFocus)
          return
        if (hovered)
          closeTimer.stop()
        else
          closeTimer.restart()
      }
    }

    // Left and Right move between previews and their close buttons; Up and Down between the rows of the list.
    Keys.onPressed: event => {
      event.accepted = false
      const focused = root.activeFocusItem
      const forward = event.key === Qt.Key_Right || event.key === Qt.Key_Down
      const along = root.listed ? event.key === Qt.Key_Up || event.key === Qt.Key_Down : event.key === Qt.Key_Left || event.key === Qt.Key_Right
      if (event.key === Qt.Key_Escape) {
        root.hide()
        root.dismissed()
      } else if (along && focused) {
        let next = focused.nextItemInFocusChain(forward)
        // In the list, Up and Down go from row to row; Tab still reaches each row's close button.
        while (root.listed && next && next !== focused && next.objectName === "previewClose")
          next = next.nextItemInFocusChain(forward)
        if (next)
          next.forceActiveFocus(Qt.TabFocusReason)
      } else {
        return
      }
      event.accepted = true
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
        id: previews
        model: root.running && !root.listed ? root.windows : []

        MouseArea {
          id: card
          required property var modelData
          objectName: "preview:" + modelData.title
          width: root.cardWidth
          height: Math.round(root.cardWidth * 160 / 216)
          hoverEnabled: true
          activeFocusOnTab: true
          onActiveFocusChanged: if (activeFocus) root.focusedWindow = modelData
          onContainsMouseChanged: root.pointerOn(containsMouse ? modelData : null)
          Keys.onPressed: event => {
            event.accepted = false
            if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
              root.activateRequested(modelData)
              event.accepted = true
            } else if (event.key === Qt.Key_Delete) {
              modelData.close()
              event.accepted = true
            }
          }
          // A list item, not a button, so Narrator also reaches the close button inside it.
          Accessible.role: Accessible.ListItem
          Accessible.name: modelData.title
          Accessible.onPressAction: root.activateRequested(modelData)
          onClicked: root.activateRequested(modelData)

          Rectangle {
            anchors.fill: parent
            radius: Theme.controlCornerRadius
            color: card.pressed ? Theme.subtleFillColorTertiary : card.containsMouse || card.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
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
            // Shown while the pointer or the keyboard is on the preview.
            visible: card.containsMouse || card.activeFocus || activeFocus
            x: parent.width - width - 4
            y: 4
          }

          WindowThumbnail {
            x: 8
            y: 32
            width: parent.width - 16
            height: parent.height - 40
            window: card.modelData
            peek: root.visible && root.peekWindow === card.modelData
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
          id: lines
          model: root.running && root.listed ? root.windows : []

          MouseArea {
            id: line
            required property var modelData
            objectName: "previewRow:" + modelData.title
            width: rows.width
            height: 40
            hoverEnabled: true
            activeFocusOnTab: true
            // The focused row scrolls into view, and stays the window the keyboard is on.
            onActiveFocusChanged: {
              if (!activeFocus)
                return
              root.focusedWindow = modelData
              if (y < list.contentY)
                list.contentY = y
              else if (y + height > list.contentY + list.height)
                list.contentY = y + height - list.height
            }
            Keys.onPressed: event => {
              event.accepted = false
              if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
                root.activateRequested(modelData)
                event.accepted = true
              } else if (event.key === Qt.Key_Delete) {
                modelData.close()
                event.accepted = true
              }
            }
            Accessible.role: Accessible.ListItem
            Accessible.name: modelData.title
            Accessible.onPressAction: root.activateRequested(modelData)
            onClicked: root.activateRequested(modelData)

            Rectangle {
              anchors.fill: parent
              radius: Theme.controlCornerRadius
              color: line.pressed ? Theme.subtleFillColorTertiary : line.containsMouse || line.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
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
              visible: line.containsMouse || line.activeFocus || activeFocus
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
    activeFocusOnTab: true
    Keys.onPressed: event => {
      event.accepted = false
      if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
        window.close()
        event.accepted = true
      }
    }
    Accessible.role: Accessible.Button
    Accessible.name: "Close " + (window ? window.title : "")
    Accessible.onPressAction: window.close()
    onClicked: window.close()

    // Windows' caption close red with a white glyph, in light and dark alike; a contrast theme highlights instead.
    Rectangle {
      objectName: "previewCloseFill"
      anchors.fill: parent
      radius: Theme.controlCornerRadius
      color: !close.containsMouse && !close.pressed ? "transparent"
        : Theme.highContrast ? Theme.systemColorHighlightColor
        : close.pressed ? "#e6c42b1c" : "#c42b1c"
    }

    Text {
      objectName: "previewCloseGlyph"
      anchors.centerIn: parent
      text: ""
      font.family: Theme.iconFontFamily
      font.pixelSize: 10
      color: !close.containsMouse && !close.pressed ? Theme.textFillColorPrimary
        : Theme.highContrast ? Theme.systemColorHighlightTextColor
        : close.pressed ? "#b3ffffff" : "#ffffff"
    }
  }
}
