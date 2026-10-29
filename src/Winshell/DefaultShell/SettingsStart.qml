import QtQuick

// Start: recommended apps on or off, and the way to the pinned apps.
Column {
  id: root

  signal pinsRequested()

  spacing: 4

  SettingsCard {
    id: recommended
    key: "startRecommended"
    focusTarget: recommendedSwitch.control

    SettingsSwitch {
      id: recommendedSwitch
      card: recommended
      name: recommended.title
      checked: Preferences.startRecommended
      onToggled: on => Preferences.startRecommended = on
    }
  }

  SettingsCard {
    key: "startPins"
    onClicked: root.pinsRequested()
  }
}
