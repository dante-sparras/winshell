import Winshell.Theme
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// Hotkeys: the shell's hotkeys, each a sequence the user types, such as Ctrl+Alt+S. Empty turns one off. A hotkey
// Windows or another app already owns says so, and is not taken from it.
Column {
  id: root

  // The Hotkey objects the shell registers, to show whether Windows took each one.
  property var startHotkey: null
  property var settingsHotkey: null

  spacing: 4

  Text {
    width: parent.width
    bottomPadding: 8
    text: "Type a key with Win, Ctrl or Alt before it, and Shift if you like, such as Ctrl+Alt+S. Win alone is the Windows key. Leave it empty to turn the hotkey off."
    wrapMode: Text.Wrap
    font: Theme.typography.body
    color: Theme.textFillColorSecondary
  }

  // Why a sequence typed is not taken, or "". The file is held to the same rule by Preferences.
  function refusal(text, other) {
    const why = Preferences.hotkeyRefusal(text)
    if (text === "" || why !== "")
      return why
    const same = s => s.toLowerCase().split(",").map(c => c.split("+").map(p => p.trim()).join("+")).join(",")
    if (other.sequence !== "" && same(text) === same(other.sequence))
      return other.title + " already uses " + other.sequence
    return ""
  }

  component HotkeyCard: SettingsCard {
    id: card
    property var hotkey: null
    property string sequence: ""
    // The other hotkey, which this one must not repeat.
    property var other: null
    // Why the last sequence typed was not taken, until the next one.
    property string refused: ""
    signal changed(string sequence)

    // Whether Windows took it, or why not.
    description: refused !== "" ? refused
      : sequence === "" ? "Off"
      : hotkey === null ? ""
      : hotkey.error !== "" ? hotkey.error
      : hotkey.taken ? "Windows or another app is using " + sequence
      : hotkey.active ? "On"
      : ""
    focusTarget: field

    TextField {
      id: field
      objectName: "sequence"
      width: 200
      text: card.sequence
      placeholderText: "None"
      Accessible.name: card.title
      Accessible.description: card.description
      function apply() {
        const next = text.trim()
        if (next !== card.sequence) {
          card.refused = root.refusal(next, card.other)
          if (card.refused === "")
            card.changed(next)
        }
        text = Qt.binding(() => card.sequence)
      }
      onAccepted: apply()
      onEditingFinished: apply()
      Keys.onEscapePressed: event => {
        text = Qt.binding(() => card.sequence)
        event.accepted = true
      }
      onActiveFocusChanged: if (activeFocus) card.reveal()
    }
  }

  HotkeyCard {
    id: startCard
    key: "hotkeyStart"
    hotkey: root.startHotkey
    other: settingsCard
    sequence: Preferences.startHotkey
    onChanged: s => Preferences.startHotkey = s
  }

  HotkeyCard {
    id: settingsCard
    key: "hotkeySettings"
    hotkey: root.settingsHotkey
    other: startCard
    sequence: Preferences.settingsHotkey
    onChanged: s => Preferences.settingsHotkey = s
  }
}
