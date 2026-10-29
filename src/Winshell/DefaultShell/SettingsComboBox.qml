import QtQuick
import QtQuick.Controls

// A combo box over a setting's values: options are [value, label] pairs, and the setting decides what shows.
ComboBox {
  id: root

  property var options: []
  property var value
  property string name: ""
  // The card to scroll into view when the box takes the keyboard.
  property var card: null

  signal picked(var value)

  objectName: "comboBox"
  implicitWidth: 200
  model: (options ?? []).map(o => o[1])
  currentIndex: Math.max(0, (options ?? []).findIndex(o => o[0] === value))
  Accessible.name: name
  onActivated: index => {
    picked(options[index][0])
    currentIndex = Qt.binding(() => Math.max(0, options.findIndex(o => o[0] === value)))
  }
  onActiveFocusChanged: if (activeFocus && card) card.reveal()
}
