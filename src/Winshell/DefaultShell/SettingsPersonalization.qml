import Winshell.Theme
import QtQuick
import "settings.js" as Settings

// Personalization: light or dark, the accent color, and the material behind the taskbar and flyouts. They change
// Winshell only; Windows' own colors are a link away.
Column {
  id: root

  spacing: 4

  readonly property bool manualAccent: Preferences.validAccent

  SettingsCard {
    id: mode
    key: "colorScheme"
    focusTarget: modeBox

    SettingsComboBox {
      id: modeBox
      card: mode
      name: mode.title
      options: [["system", "Same as Windows"], ["light", "Light"], ["dark", "Dark"]]
      value: Preferences.colorScheme
      onPicked: v => Preferences.colorScheme = v
    }
  }

  SettingsCard {
    id: accent
    key: "accentColor"
    focusTarget: accentBox

    SettingsComboBox {
      id: accentBox
      card: accent
      name: accent.title
      options: [["windows", "Same as Windows"], ["manual", "Manual"]]
      value: root.manualAccent ? "manual" : "windows"
      // Manual starts from the accent in use, which is Windows' until the user picks another.
      onPicked: v => Preferences.accentColor = v === "manual" ? String(Theme.systemAccentColor).toUpperCase() : ""
    }
  }

  // The Windows accent colors, as Settings shows them under a manual accent.
  Rectangle {
    id: swatches
    objectName: "accentSwatches"
    visible: root.manualAccent
    width: parent.width
    height: grid.height + 32
    radius: Theme.controlCornerRadius
    color: Theme.cardBackgroundFillColorSecondary
    border.width: 1
    border.color: Theme.cardStrokeColorDefault
    Accessible.role: Accessible.List
    Accessible.name: "Windows colors"

    function focusSwatch(index) {
      const accent = Settings.accents[Math.max(0, Math.min(Settings.accents.length - 1, index))]
      const swatch = grid.children.find(c => c.objectName === "swatch:" + accent[0])
      if (swatch)
        swatch.forceActiveFocus()
    }

    Grid {
      id: grid
      x: 52
      y: 16
      columns: 8
      spacing: 4

      Repeater {
        model: Settings.accents

        Rectangle {
          id: swatch
          required property var modelData
          required property int index
          readonly property bool chosen: String(Preferences.accentColor).toUpperCase() === modelData[0]
          objectName: "swatch:" + modelData[0]
          width: 40
          height: 40
          radius: Theme.controlCornerRadius
          color: modelData[0]
          border.width: chosen || swatch.activeFocus ? 2 : 0
          border.color: Theme.textFillColorPrimary
          activeFocusOnTab: chosen || (index === 0 && !Settings.accents.some(a => a[0] === String(Preferences.accentColor).toUpperCase()))
          Accessible.role: Accessible.RadioButton
          Accessible.name: modelData[1]
          Accessible.checkable: true
          Accessible.checked: chosen
          Accessible.onPressAction: Preferences.accentColor = modelData[0]

          function pick() { Preferences.accentColor = modelData[0] }

          onActiveFocusChanged: if (activeFocus) accent.reveal()
          Keys.onReturnPressed: pick()
          Keys.onSpacePressed: pick()
          Keys.onLeftPressed: swatches.focusSwatch(index - 1)
          Keys.onRightPressed: swatches.focusSwatch(index + 1)
          Keys.onUpPressed: swatches.focusSwatch(index - grid.columns)
          Keys.onDownPressed: swatches.focusSwatch(index + grid.columns)

          Text {
            anchors.centerIn: parent
            visible: swatch.chosen
            text: ""
            font.family: Theme.iconFontFamily
            font.pixelSize: 16
            // White or black, whichever reads on the swatch.
            color: swatch.color.hslLightness > 0.6 ? "black" : "white"
            Accessible.ignored: true
          }

          MouseArea {
            anchors.fill: parent
            onClicked: swatch.pick()
          }
        }
      }
    }
  }

  SettingsCard {
    id: material
    key: "material"
    focusTarget: materialBox

    SettingsComboBox {
      id: materialBox
      card: material
      name: material.title
      options: [["acrylic", "Acrylic"], ["mica", "Mica"]]
      value: Preferences.backdrop === "mica" ? "mica" : "acrylic"
      onPicked: v => Preferences.backdrop = v
    }
  }

  SettingsHeader { text: "Related settings" }

  SettingsCard { key: "windowsColors" }
}
