import Winshell
import Winshell.Apps
import Winshell.Input
import Winshell.Theme
import Winshell.Windows
import QtQuick

// Alt+Tab and the desktop chords. The shell builds this only while Preferences.desktopChords is on, so these hotkeys
// are not registered when it is off. The switcher is a popup and stays hidden until a chord opens it.
// Letting go of Alt does not activate a window: this hotkey is already up when Tab comes up, even while Alt is held.
// The cards follow WindowList order, the order windows opened, not most-recently used.
// Win+D minimizes and restores. It draws nothing on the taskbar.
// Win+X does not shut down or start Task Manager.
Scope {
  id: root

  property var taskbar: null
  property int selected: 0
  property var parked: []

  signal openSettings()
  signal openSearch()

  function windows() {
    return WindowList.windows.values
  }

  function indexOf(window) {
    var list = windows()
    for (var i = 0; i < list.length; i++) {
      if (list[i] === window)
        return i
    }
    return -1
  }

  function cycle(step) {
    var list = windows()
    if (list.length === 0) {
      selected = 0
    } else if (!switcher.visible) {
      var start = indexOf(WindowList.activeWindow)
      selected = start < 0 ? (step > 0 ? 0 : list.length - 1) : (start + step + list.length) % list.length
    } else {
      selected = (selected + step + list.length) % list.length
    }
    openSwitcher()
  }

  function toggleTaskView() {
    if (switcher.visible) {
      switcher.visible = false
      return
    }
    var start = indexOf(WindowList.activeWindow)
    selected = start >= 0 ? start : 0
    openSwitcher()
  }

  function openSwitcher() {
    if (!taskbar)
      return
    power.visible = false
    switcher.visible = true
    Qt.callLater(focusChosen)
  }

  function focusChosen() {
    if (!switcher.visible)
      return
    var item = cardRepeater.itemAt(selected)
    if (item)
      item.forceActiveFocus()
    else
      frame.forceActiveFocus()
  }

  function closeSwitcher() {
    switcher.visible = false
  }

  function choose(window) {
    if (window)
      window.activate()
    switcher.visible = false
  }

  // The first press minimizes every window that is open and remembers those. The next press restores them.
  function showDesktop() {
    switcher.visible = false
    power.visible = false
    var list = windows()
    var open = []
    for (var i = 0; i < list.length; i++) {
      if (!list[i].minimized)
        open.push(list[i])
    }
    if (open.length > 0) {
      parked = open
      for (var j = 0; j < open.length; j++)
        open[j].minimized = true
      return
    }
    var still = []
    for (var k = 0; k < parked.length; k++) {
      if (indexOf(parked[k]) >= 0)
        still.push(parked[k])
    }
    for (var n = 0; n < still.length; n++) {
      if (still[n].minimized)
        still[n].minimized = false
    }
    parked = []
  }

  function togglePower() {
    switcher.visible = false
    if (power.visible) {
      power.visible = false
      return
    }
    if (!taskbar)
      return
    power.visible = true
    Qt.callLater(focusMenu)
  }

  function focusMenu() {
    if (power.visible && menuRepeater.count > 0)
      menuRepeater.itemAt(0).forceActiveFocus()
  }

  function runCommand(kind, url) {
    power.visible = false
    if (kind === "url") {
      Qt.openUrlExternally(url)
    } else if (kind === "explorer") {
      var app = Apps.find("Microsoft.Windows.Explorer") || Apps.find("File Explorer")
      if (app)
        app.launch()
    } else if (kind === "search") {
      openSearch()
    } else if (kind === "desktop") {
      showDesktop()
    } else if (kind === "settings") {
      openSettings()
    }
  }

  Hotkey {
    sequence: "Alt+Tab"
    onPressed: root.cycle(1)
  }

  Hotkey {
    sequence: "Alt+Shift+Tab"
    onPressed: root.cycle(-1)
  }

  Hotkey {
    sequence: "Win+Tab"
    onPressed: root.toggleTaskView()
  }

  Hotkey {
    sequence: "Win+D"
    onPressed: root.showDesktop()
  }

  Hotkey {
    sequence: "Win+X"
    onPressed: root.togglePower()
  }

  PopupWindow {
    id: switcher
    objectName: "windowSwitcher"
    visible: false
    color: "transparent"
    grabFocus: true
    WindowEffects.backdrop: Preferences.material
    WindowEffects.corners: WindowEffects.RoundCorners
    anchor.window: root.taskbar
    anchor.rect.x: root.taskbar ? root.taskbar.width / 2 : 0
    anchor.rect.y: 0
    anchor.edges: Edges.Top
    anchor.gravity: Edges.Top
    anchor.margins.top: 12
    implicitWidth: column.implicitWidth + 16
    implicitHeight: column.implicitHeight + 16

    readonly property real room: Math.max(200, (root.taskbar && root.taskbar.screen ? root.taskbar.screen.width : 1920) - 48)

    Item {
      id: frame
      anchors.fill: parent
      Accessible.role: Accessible.Dialog
      Accessible.name: "Task switcher"
      Keys.onEscapePressed: root.closeSwitcher()

      Column {
        id: column
        x: 8
        y: 8
        spacing: 8

        Text {
          objectName: "switcherEmpty"
          visible: cardRepeater.count === 0
          width: Math.max(implicitWidth, 200)
          text: "No open windows"
          font: Theme.typography.body
          color: Theme.textFillColorPrimary
        }

        Flickable {
          id: strip
          visible: cardRepeater.count > 0
          width: Math.min(Math.max(cards.implicitWidth, 1), switcher.room)
          height: Math.max(cards.implicitHeight, 1)
          contentWidth: cards.implicitWidth
          contentHeight: cards.implicitHeight
          flickableDirection: Flickable.HorizontalFlick
          boundsBehavior: Flickable.StopAtBounds
          clip: true
          interactive: contentWidth > width

          Row {
            id: cards
            spacing: 8

            Repeater {
              id: cardRepeater
              model: WindowList.windows

              MouseArea {
                id: card
                required property var modelData
                required property int index
                readonly property bool chosen: index === root.selected
                objectName: "switch:" + modelData.title
                width: 200
                height: 140
                hoverEnabled: true
                activeFocusOnTab: true
                Accessible.role: Accessible.ListItem
                Accessible.name: modelData.title
                Accessible.onPressAction: root.choose(modelData)
                onClicked: root.choose(modelData)
                Keys.onEscapePressed: root.closeSwitcher()
                Keys.onReturnPressed: root.choose(modelData)
                Keys.onEnterPressed: root.choose(modelData)
                Keys.onSpacePressed: root.choose(modelData)
                Keys.onLeftPressed: root.cycle(-1)
                Keys.onRightPressed: root.cycle(1)

                Rectangle {
                  anchors.fill: parent
                  radius: Theme.controlCornerRadius
                  color: card.pressed ? Theme.subtleFillColorTertiary : card.chosen || card.containsMouse || card.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
                  border.width: card.chosen ? 2 : 0
                  border.color: Theme.focusStrokeColorOuter
                }

                Image {
                  id: icon
                  x: 8
                  y: 8
                  width: 16
                  height: 16
                  sourceSize: Qt.size(16, 16)
                  source: card.modelData.icon ? Shell.iconPath(card.modelData.icon) : ""
                }

                Text {
                  anchors.left: icon.right
                  anchors.leftMargin: 8
                  anchors.right: parent.right
                  anchors.rightMargin: 8
                  anchors.verticalCenter: icon.verticalCenter
                  text: card.modelData.title
                  elide: Text.ElideRight
                  font: Theme.typography.caption
                  color: Theme.textFillColorPrimary
                }

                WindowThumbnail {
                  x: 8
                  y: 32
                  width: parent.width - 16
                  height: parent.height - 40
                  window: card.modelData
                }
              }
            }
          }
        }

        Row {
          id: desks
          visible: VirtualDesktops.supported
          spacing: 8

          Repeater {
            model: VirtualDesktops.supported ? VirtualDesktops.desktops : []

            MouseArea {
              id: desk
              required property var modelData
              objectName: "desktop:" + modelData.name
              width: Math.max(96, deskLabel.implicitWidth + 24)
              height: 32
              hoverEnabled: true
              activeFocusOnTab: true
              Accessible.role: Accessible.Button
              Accessible.name: modelData.name
              Accessible.onPressAction: VirtualDesktops.switchTo(modelData)
              onClicked: VirtualDesktops.switchTo(modelData)
              Keys.onEscapePressed: root.closeSwitcher()
              Keys.onReturnPressed: VirtualDesktops.switchTo(modelData)
              Keys.onSpacePressed: VirtualDesktops.switchTo(modelData)

              Rectangle {
                anchors.fill: parent
                radius: Theme.controlCornerRadius
                color: desk.pressed ? Theme.subtleFillColorTertiary : modelData.current || desk.containsMouse || desk.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
              }

              Text {
                id: deskLabel
                anchors.centerIn: parent
                text: desk.modelData.name
                font: Theme.typography.body
                color: Theme.textFillColorPrimary
              }
            }
          }
        }
      }
    }
  }

  ListModel {
    id: commands
    ListElement { name: "Installed apps"; url: "ms-settings:appsfeatures"; kind: "url" }
    ListElement { name: "System"; url: "ms-settings:system"; kind: "url" }
    ListElement { name: "Network"; url: "ms-settings:network"; kind: "url" }
    ListElement { name: "Settings"; url: "ms-settings:"; kind: "url" }
    ListElement { name: "File Explorer"; url: ""; kind: "explorer" }
    ListElement { name: "Search"; url: ""; kind: "search" }
    ListElement { name: "Desktop"; url: ""; kind: "desktop" }
    ListElement { name: "Taskbar settings"; url: ""; kind: "settings" }
  }

  PopupWindow {
    id: power
    objectName: "powerMenu"
    visible: false
    color: "transparent"
    grabFocus: true
    WindowEffects.backdrop: Preferences.material
    WindowEffects.corners: WindowEffects.RoundCorners
    anchor.window: root.taskbar
    anchor.rect.x: 0
    anchor.rect.y: 0
    anchor.edges: Edges.Top | Edges.Left
    anchor.gravity: Edges.Top | Edges.Right
    anchor.margins.top: 8
    implicitWidth: 280
    implicitHeight: menuColumn.implicitHeight + 8

    Item {
      anchors.fill: parent
      Accessible.role: Accessible.PopupMenu
      Accessible.name: "Power user menu"

      Column {
        id: menuColumn
        x: 4
        y: 4
        spacing: 0

        Repeater {
          id: menuRepeater
          model: commands

          MouseArea {
            id: command
            required property string name
            required property string url
            required property string kind
            objectName: "power:" + name
            width: 272
            height: 36
            hoverEnabled: true
            Accessible.role: Accessible.MenuItem
            Accessible.name: name
            Accessible.onPressAction: root.runCommand(kind, url)
            onClicked: root.runCommand(kind, url)
            Keys.onEscapePressed: power.visible = false
            Keys.onReturnPressed: root.runCommand(kind, url)
            Keys.onSpacePressed: root.runCommand(kind, url)

            Rectangle {
              anchors.fill: parent
              radius: Theme.controlCornerRadius
              color: command.pressed ? Theme.subtleFillColorTertiary : command.containsMouse || command.activeFocus ? Theme.subtleFillColorSecondary : "transparent"
            }

            Text {
              x: 12
              anchors.verticalCenter: parent.verticalCenter
              text: command.name
              font: Theme.typography.body
              color: Theme.textFillColorPrimary
            }
          }
        }
      }
    }
  }
}
