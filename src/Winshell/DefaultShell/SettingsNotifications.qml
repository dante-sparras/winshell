import Winshell.Notifications
import Winshell.Theme
import QtQuick

// Notifications: Do not disturb as Windows reports it, and per app whether the notification center shows its
// notifications. Windows decides banners, sounds and Do not disturb; those are a link to its Notifications page.
Column {
  id: root

  property var listener: null

  // The apps with notifications now, and the ones turned off before, by name.
  readonly property var found: {
    const seen = new Map()
    const groups = listener ? listener.groups.values : []
    for (const group of groups) {
      if (group.appId)
        seen.set(group.appId, group.appName || group.appId)
    }
    for (const app of Preferences.hiddenNotificationApps ?? []) {
      if (app && app.id && !seen.has(app.id))
        seen.set(app.id, app.name || app.id)
    }
    return Array.from(seen, ([id, name]) => ({ id: id, name: name }))
  }

  // The list only grows while the window is open, so turning an app on or off keeps its card, and the keyboard on it.
  property var apps: []

  function merge() {
    const added = (found ?? []).filter(app => !apps.some(a => a.id === app.id))
    if (added.length > 0)
      apps = apps.concat(added).sort((a, b) => a.name.localeCompare(b.name))
  }

  onFoundChanged: merge()
  Component.onCompleted: merge()

  readonly property string doNotDisturb: listener === null ? "Unknown"
    : listener.doNotDisturbState === NotificationState.On ? "On"
    : listener.doNotDisturbState === NotificationState.Off ? "Off"
    : "Unknown"

  spacing: 4

  SettingsCard {
    key: "doNotDisturb"

    Text {
      objectName: "doNotDisturbState"
      anchors.verticalCenter: parent.verticalCenter
      text: root.doNotDisturb
      font: Theme.typography.body
      color: Theme.textFillColorSecondary
    }
  }

  SettingsHeader { text: "Notifications from apps" }

  SettingsCard { key: "notificationApps" }

  Text {
    width: parent.width
    visible: root.apps.length === 0
    topPadding: 8
    text: "Apps show up here once they have sent a notification."
    wrapMode: Text.Wrap
    font: Theme.typography.body
    color: Theme.textFillColorSecondary
  }

  Repeater {
    model: root.apps

    SettingsCard {
      id: app
      required property var modelData
      objectName: "app:" + modelData.id
      key: "notificationApps"
      title: modelData.name
      description: ""
      glyph: ""
      focusTarget: appSwitch.control

      SettingsSwitch {
        id: appSwitch
        card: app
        name: "Show notifications from " + app.modelData.name
        checked: Preferences.showsNotificationsFrom(app.modelData.id)
        onToggled: on => Preferences.setNotificationsFrom(app.modelData.id, app.modelData.name, on)
      }
    }
  }
}
