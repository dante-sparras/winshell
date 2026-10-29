import Winshell
import Winshell.Theme
import QtQuick
import "tasks.js" as Tasks

// One app on the taskbar, as Windows 11 draws it: its icon on a subtle fill when hovered or in front, and a line
// under it while it runs, longer and in the accent color while one of its windows is in front. A window that
// flashes for attention turns the button red until it is activated.
MouseArea {
  id: root

  required property var task
  readonly property var windows: task ? task.windows : []
  readonly property bool running: windows.length > 0
  readonly property bool active: windows.some(w => w.activated && !w.minimized)
  readonly property bool attention: windows.some(w => w.attention)

  // A left click or Narrator's invoke, a middle or Shift click for a new window, and a right click for the menu.
  signal invoked()
  signal launchRequested()
  signal menuRequested()

  implicitWidth: 40
  implicitHeight: 40
  hoverEnabled: true
  acceptedButtons: Qt.LeftButton | Qt.MiddleButton | Qt.RightButton

  Accessible.role: Accessible.Button
  Accessible.name: task ? Tasks.accessibleName(task) : ""
  Accessible.description: attention ? "Needs your attention" : ""
  Accessible.onPressAction: root.invoked()

  onClicked: mouse => {
    if (mouse.button === Qt.RightButton)
      root.menuRequested()
    else if (mouse.button === Qt.MiddleButton || (mouse.modifiers & Qt.ShiftModifier))
      root.launchRequested()
    else
      root.invoked()
  }

  Rectangle {
    objectName: "taskBackground"
    anchors.fill: parent
    radius: Theme.controlCornerRadius
    color: root.attention ? Theme.systemFillColorCriticalBackground
      : root.pressed ? Theme.subtleFillColorTertiary
      : root.containsMouse || root.active ? Theme.subtleFillColorSecondary
      : "transparent"
    border.width: root.active || root.attention ? 1 : 0
    border.color: Theme.controlStrokeColorDefault

    Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
  }

  Image {
    objectName: "taskIcon"
    anchors.centerIn: parent
    width: 24
    height: 24
    sourceSize: Qt.size(24, 24)
    source: root.task && root.task.icon ? Shell.iconPath(root.task.icon) : ""
    scale: root.pressed ? 0.85 : 1

    Behavior on scale { NumberAnimation { duration: Theme.controlFasterAnimationDuration } }
  }

  Rectangle {
    objectName: "taskIndicator"
    visible: root.running
    anchors.horizontalCenter: parent.horizontalCenter
    anchors.bottom: parent.bottom
    anchors.bottomMargin: 2
    width: root.active ? 16 : 6
    height: 3
    radius: 1.5
    color: root.attention ? Theme.systemFillColorCritical
      : root.active ? Theme.accentFillColorDefault
      : Theme.textFillColorTertiary

    Behavior on width { NumberAnimation { duration: Theme.controlNormalAnimationDuration; easing.type: Easing.OutCubic } }
  }
}
