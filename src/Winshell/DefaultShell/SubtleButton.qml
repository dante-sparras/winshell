import QtQuick

// Return, numpad Enter and Space, and Narrator's invoke, for a button that keeps its own fill.
MouseArea {
  id: control

  // Off for a button whose pointer buttons each do something of their own.
  property bool activateOnClick: true
  // Off for a button that gives Shift+Enter a meaning of its own, in its Keys.onPressed.
  property bool shiftEnterActivates: true

  signal activated()

  Accessible.role: Accessible.Button
  Accessible.onPressAction: control.activated()
  onClicked: if (control.activateOnClick) control.activated()

  Keys.onReturnPressed: event => control.enterPressed(event)
  Keys.onEnterPressed: event => control.enterPressed(event)
  Keys.onSpacePressed: control.activated()

  function enterPressed(event) {
    if (control.shiftEnterActivates || !(event.modifiers & Qt.ShiftModifier))
      control.activated()
    else
      event.accepted = false
  }
}
