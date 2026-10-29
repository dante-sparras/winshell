import Winshell.Theme
import QtQuick
import QtQuick.Controls

// A toggle switch as a settings card has it: "On" or "Off" to the left of the switch.
Row {
  id: root

  property bool checked: false
  property string name: ""
  // The card to scroll into view when the switch takes the keyboard.
  property var card: null
  readonly property alias control: toggle

  signal toggled(bool checked)

  spacing: 12

  Text {
    anchors.verticalCenter: parent.verticalCenter
    text: root.checked ? "On" : "Off"
    font: Theme.typography.body
    color: toggle.enabled ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
    Accessible.ignored: true
  }

  Switch {
    id: toggle
    objectName: "switch"
    anchors.verticalCenter: parent.verticalCenter
    checked: root.checked
    enabled: root.enabled
    Accessible.name: root.name
    // The setting decides: a change it refuses puts the switch back.
    onToggled: {
      root.toggled(checked)
      checked = Qt.binding(() => root.checked)
    }
    onActiveFocusChanged: if (activeFocus && root.card) root.card.reveal()
  }
}
