import Winshell.Notifications
import Winshell.Theme
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ColumnLayout {
  id: root
  property var listener: null
  spacing: 8

  RowLayout {
    Layout.fillWidth: true
    Text { Layout.fillWidth: true; text: "Notifications"; font.pixelSize: 20; font.weight: Font.DemiBold; color: Theme.textFillColorPrimary }
    Button { text: "Clear all"; flat: true; enabled: root.listener !== null && root.listener.notifications.values.length > 0; onClicked: root.listener.clearAll() }
  }

  Text {
    Layout.fillWidth: true
    text: root.listener === null ? "Notification access unavailable" : "Do not disturb: " + (root.listener.doNotDisturbState === NotificationState.On ? "On" : root.listener.doNotDisturbState === NotificationState.Off ? "Off" : "Unknown") + " · Focus: " + (root.listener.focusState === NotificationState.On ? "On" : root.listener.focusState === NotificationState.Off ? "Off" : "Unknown")
    font.pixelSize: 12
    wrapMode: Text.Wrap
    color: Theme.textFillColorSecondary
  }

  Button {
    visible: root.listener !== null && root.listener.accessStatus === NotificationAccessStatus.Unspecified
    text: "Allow notification access"
    onClicked: root.listener.requestAccess()
  }
  Text {
    Layout.fillWidth: true
    visible: root.listener !== null && root.listener.accessStatus === NotificationAccessStatus.Requesting
    text: "Waiting for Windows permission…"
    color: Theme.textFillColorSecondary
  }
  Text {
    Layout.fillWidth: true
    visible: root.listener !== null && root.listener.error !== ""
    text: root.listener === null ? "" : root.listener.error
    wrapMode: Text.Wrap
    color: Theme.textFillColorPrimary
  }
  Text {
    Layout.fillWidth: true
    visible: root.listener !== null && root.listener.accessStatus === NotificationAccessStatus.Allowed && root.listener.notifications.values.length === 0
    text: "You're all caught up"
    color: Theme.textFillColorSecondary
  }

  FocusScrollView {
    id: scroll
    Layout.fillWidth: true
    Layout.fillHeight: true
    contentWidth: availableWidth
    clip: true
    Column {
      width: scroll.availableWidth
      spacing: 12
      Repeater {
        model: root.listener === null ? [] : root.listener.groups
        Column {
          id: group
          required property var modelData
          width: scroll.availableWidth
          spacing: 4
          RowLayout {
            width: group.width
            Text { Layout.fillWidth: true; text: group.modelData.appName || "Unknown app"; font.weight: Font.DemiBold; color: Theme.textFillColorPrimary; wrapMode: Text.Wrap }
            Button { text: "Clear"; flat: true; Accessible.name: "Clear notifications from " + group.modelData.appName; onClicked: group.modelData.dismiss(); onActiveFocusChanged: if (activeFocus) scroll.reveal(this) }
          }
          Repeater {
            model: group.modelData.notifications
            Rectangle {
              id: card
              required property var modelData
              width: group.width
              height: cardContent.implicitHeight + 24
              radius: 8
              color: Theme.cardBackgroundFillColorDefault
              border.width: 1
              border.color: Theme.controlStrokeColorDefault
              ColumnLayout {
                id: cardContent
                x: 12; y: 12; width: parent.width - 24
                spacing: 4
                RowLayout {
                  Layout.fillWidth: true
                  Text { Layout.fillWidth: true; text: card.modelData.title; font.weight: Font.DemiBold; wrapMode: Text.Wrap; color: Theme.textFillColorPrimary }
                  Button { text: ""; font.family: Theme.iconFontFamily; padding: 0; implicitWidth: 32; flat: true; Accessible.name: "Dismiss " + card.modelData.title; onClicked: card.modelData.dismiss(); onActiveFocusChanged: if (activeFocus) scroll.reveal(this) }
                }
                Text { Layout.fillWidth: true; text: card.modelData.body; wrapMode: Text.Wrap; color: Theme.textFillColorPrimary }
                Text { text: Qt.formatDateTime(card.modelData.createdAt, "ddd HH:mm"); font.pixelSize: 12; color: Theme.textFillColorSecondary }
              }
            }
          }
        }
      }
    }
  }
}
