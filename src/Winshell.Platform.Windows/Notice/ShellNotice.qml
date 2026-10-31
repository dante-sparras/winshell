import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls

// Winshell's own notice, drawn like a Windows 11 notification: bottom right of the main screen, above the taskbar,
// Acrylic, with the app name, a title, the text and two buttons. It is not part of a configuration, so it shows
// even when the configuration did not load. It does not take the keyboard when it appears; a click anywhere on it
// gives it the keyboard, and then Tab moves between its buttons and Escape dismisses it. Pointing at it, or having
// the keyboard, holds a timed notice open.
PanelWindow {
  id: root

  objectName: "notice"

  property string title: ""
  property string detail: ""
  property string body: ""
  property string note: ""
  property string actionText: ""
  // How long the notice stays, in milliseconds; 0 keeps it until it is dismissed.
  property int timeout: 0

  signal actionRequested()
  signal dismissRequested()

  readonly property bool contrast: Theme.highContrast
  readonly property string announcement: [title, detail, body, note].filter(t => t.length > 0).join(". ")

  implicitWidth: 364
  implicitHeight: content.implicitHeight + 32
  color: "transparent"
  anchors.bottom: true
  anchors.right: true
  margins.bottom: 12
  margins.right: 12
  exclusiveZone: 0
  aboveWindows: true
  focusable: true
  WindowEffects.backdrop: WindowEffects.Acrylic
  WindowEffects.corners: WindowEffects.RoundCorners

  Timer {
    id: hideTimer
    objectName: "hideTimer"
    interval: root.timeout
    running: root.timeout > 0 && !hover.containsMouse && root.activeFocusItem === null
    onTriggered: root.dismissRequested()
  }

  Rectangle {
    id: card
    objectName: "card"
    anchors.fill: parent
    radius: Theme.overlayCornerRadius
    color: "transparent"
    border.width: root.contrast ? 2 : 1
    border.color: Theme.surfaceStrokeColorFlyout
    Accessible.role: Accessible.Notification
    Accessible.name: root.announcement

    Keys.onEscapePressed: root.dismissRequested()

    // Under the buttons: a click on the text or the background gives the notice the keyboard, so Escape works.
    MouseArea {
      id: hover
      objectName: "hover"
      anchors.fill: parent
      hoverEnabled: true
      acceptedButtons: Qt.LeftButton
      onPressed: card.forceActiveFocus()
    }

    ColumnLayout {
      id: content
      anchors.left: parent.left
      anchors.right: parent.right
      anchors.top: parent.top
      anchors.margins: 16
      spacing: 4

      RowLayout {
        Layout.fillWidth: true
        Layout.bottomMargin: 8
        Text {
          objectName: "appName"
          Layout.fillWidth: true
          text: "Winshell"
          font: Theme.typography.caption
          color: Theme.textFillColorSecondary
        }
        Button {
          id: close
          objectName: "close"
          text: ""
          font.family: Theme.iconFontFamily
          font.pixelSize: 10
          flat: true
          padding: 0
          implicitWidth: 32
          implicitHeight: 24
          Accessible.name: "Dismiss"
          onClicked: root.dismissRequested()
          Keys.onEscapePressed: root.dismissRequested()
        }
      }

      Text {
        objectName: "title"
        Layout.fillWidth: true
        text: root.title
        font: Theme.typography.bodyStrong
        wrapMode: Text.Wrap
        color: Theme.textFillColorPrimary
      }

      Text {
        objectName: "detail"
        Layout.fillWidth: true
        visible: root.detail.length > 0
        text: root.detail
        font: Theme.typography.caption
        wrapMode: Text.Wrap
        color: Theme.textFillColorSecondary
      }

      Text {
        objectName: "body"
        Layout.fillWidth: true
        visible: root.body.length > 0
        text: root.body
        font: Theme.typography.body
        wrapMode: Text.Wrap
        // A long script error stops at about six lines; the file it names has the rest.
        Layout.maximumHeight: 120
        clip: true
        color: Theme.textFillColorPrimary
      }

      Text {
        objectName: "note"
        Layout.fillWidth: true
        visible: root.note.length > 0
        text: root.note
        font: Theme.typography.body
        wrapMode: Text.Wrap
        color: Theme.textFillColorSecondary
      }

      RowLayout {
        Layout.fillWidth: true
        Layout.topMargin: 12
        spacing: 8
        Button {
          id: action
          objectName: "action"
          Layout.fillWidth: true
          Layout.preferredWidth: 1
          visible: root.actionText.length > 0
          text: root.actionText
          highlighted: true
          Accessible.name: root.actionText
          onClicked: root.actionRequested()
          Keys.onEscapePressed: root.dismissRequested()
        }
        Button {
          id: dismiss
          objectName: "dismiss"
          Layout.fillWidth: true
          Layout.preferredWidth: 1
          text: "Dismiss"
          Accessible.name: "Dismiss"
          onClicked: root.dismissRequested()
          Keys.onEscapePressed: root.dismissRequested()
        }
      }
    }
  }
}
