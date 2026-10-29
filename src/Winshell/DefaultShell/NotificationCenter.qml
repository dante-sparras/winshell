import Winshell
import Winshell.Notifications
import Winshell.Theme
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ColumnLayout {
  id: root
  property var listener: null
  // An app turned off in Winshell Settings is left out, and Clear all leaves its notifications alone.
  readonly property var visibleGroups: listener === null ? [] : listener.groups.values.filter(g => Preferences.showsNotificationsFrom(g.appId))
  spacing: 8

  // Same regional short time as the taskbar clock. The weekday stays only after that local day.
  SystemClock { id: clock; precision: SystemClock.Minutes }

  function notificationTime(createdAt) {
    var time = Qt.formatTime(createdAt, Qt.DefaultLocaleShortDate)
    if (Qt.formatDateTime(createdAt, "yyyy-MM-dd") === Qt.formatDateTime(clock.date, "yyyy-MM-dd"))
      return time
    return Qt.formatDateTime(createdAt, "ddd") + " " + time
  }

  RowLayout {
    Layout.fillWidth: true
    Text { Layout.fillWidth: true; text: "Notifications"; font.pixelSize: 20; font.weight: Font.DemiBold; color: Theme.textFillColorPrimary }
    Button { text: "Clear all"; flat: true; enabled: root.visibleGroups.length > 0; onClicked: root.visibleGroups.forEach(g => g.dismiss()) }
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
    visible: root.listener !== null && root.listener.accessStatus === NotificationAccessStatus.Allowed && root.visibleGroups.length === 0
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
        model: root.visibleGroups
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
                Text { text: root.notificationTime(card.modelData.createdAt); font.pixelSize: 12; color: Theme.textFillColorSecondary }
              }
            }
          }
        }
      }
    }
  }
}
