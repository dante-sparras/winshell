import Winshell.Theme
import QtQuick

// One app's icon in the notification area. Clicks reach the app the way Explorer sends them; a right click asks
// the app for its menu at the icon.
MouseArea {
  id: root

  required property var item
  objectName: "trayIcon"
  // The window the icon is in, for the app's menu.
  property var window: null

  implicitWidth: 32
  implicitHeight: 40
  hoverEnabled: true
  acceptedButtons: Qt.LeftButton | Qt.MiddleButton | Qt.RightButton
  activeFocusOnTab: true

  function showMenu() {
    const point = mapToItem(null, width / 2, 0)
    item.display(window, point.x, point.y)
  }

  Keys.onPressed: event => {
    event.accepted = false
    if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space)
      item.click()
    else if (event.key === Qt.Key_Menu || (event.key === Qt.Key_F10 && (event.modifiers & Qt.ShiftModifier)))
      showMenu()
    else
      return
    event.accepted = true
  }

  Accessible.role: Accessible.Button
  Accessible.name: item ? (item.tooltip || item.appName).split("\n")[0] : ""
  Accessible.onPressAction: item.click()

  onClicked: mouse => {
    if (mouse.button === Qt.RightButton) {
      showMenu()
    } else if (mouse.button === Qt.MiddleButton) {
      item.middleClick()
    } else {
      item.click()
    }
  }
  onDoubleClicked: mouse => { if (mouse.button === Qt.LeftButton) item.doubleClick() }

  Rectangle {
    anchors.fill: parent
    anchors.topMargin: 4
    anchors.bottomMargin: 4
    radius: Theme.controlCornerRadius
    color: root.pressed ? Theme.subtleFillColorTertiary : root.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
  }

  Image {
    anchors.centerIn: parent
    width: 16
    height: 16
    sourceSize: Qt.size(16, 16)
    source: root.item ? root.item.icon : ""
  }
}
