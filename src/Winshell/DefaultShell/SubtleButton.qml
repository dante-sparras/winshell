import QtQuick

// Return, numpad Enter and Space, and Narrator's invoke, for a button that keeps its own fill. A button whose
// pointer does something of its own sets activateOnClick off. Shift+Return stays with the caller's Keys handler
// when shiftEnterActivates is off, and a control that never took numpad Enter sets enterKey off.
MouseArea {
  id: control

  property bool returnKey: true
  // Numpad Enter. The main Enter key is returnKey.
  property bool enterKey: true
  property bool spaceKey: true
  property bool shiftEnterActivates: true
  property bool activateOnClick: true

  signal activated()

  Accessible.role: Accessible.Button
  Accessible.onPressAction: control.activated()
  onClicked: if (control.activateOnClick) control.activated()

  Keys.onReturnPressed: event => control.typed(event, control.returnKey, true)
  Keys.onEnterPressed: event => control.typed(event, control.enterKey, true)
  Keys.onSpacePressed: event => control.typed(event, control.spaceKey, false)

  function typed(event, enabled, enter) {
    if (!enabled || (enter && !control.shiftEnterActivates && (event.modifiers & Qt.ShiftModifier))) {
      event.accepted = false
      return
    }
    control.activated()
  }
}
