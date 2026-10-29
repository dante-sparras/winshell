import Winshell
import Winshell.Apps
import Winshell.Theme
import Winshell.Windows
import QtQuick

// The Windows 11 taskbar along the bottom of one screen: Start, Search and the apps in the middle (or on the
// left), the notification area and the clock on the right. It reserves its edge, so maximized windows stop above
// it, unless it hides itself.
PanelWindow {
  id: root

  // The settings Settings > Personalization > Taskbar has.
  // Apps in the middle, or on the left.
  property bool centered: true
  // Slide away until the pointer reaches the bottom of the screen.
  property bool autoHide: false
  // Where running apps show with several screens: "all" taskbars, the "main" taskbar and the one where the window
  // is, or only the taskbar "where" the window is. The main taskbar is the primary screen's.
  property string appsOn: "all"

  // The pinned apps (TaskbarPins), and the notifications for the clock area.
  property var pins: null
  property var notificationListener: null

  // Hooks for the surfaces the taskbar opens. Start and Search emit a signal with their button, to anchor a
  // popup to. While startOpen is true, Start looks pressed. trayArea and clockArea replace the right side.
  property bool startOpen: false
  property bool searchOpen: false
  signal startRequested(Item button)
  signal searchRequested(Item button)
  // Taskbar settings, from the menu a right click on an empty part of the taskbar opens.
  signal settingsRequested()
  property Component trayArea: Component { TrayArea { window: root } }
  property Component clockArea: Component { TaskbarClock { notificationListener: root.notificationListener } }

  readonly property bool isMainScreen: screen !== null && screen.x === 0 && screen.y === 0
  readonly property alias startButton: start
  // Where Start sits along the taskbar, for a popup window that opens from it.
  readonly property real startX: middle.x
  readonly property alias searchButton: search
  readonly property alias taskList: taskList

  // The keyboard is in the taskbar: after Win+T (focusApps), or Narrator moving focus to one of its buttons. It
  // stays until Escape, which goes back to the window that had it, or until another window takes it.
  property bool keyboardActive: false
  property var keyboardReturn: null
  focusable: keyboardActive

  // The first app, or Start when there is none.
  function focusApps() {
    const first = search.nextItemInFocusChain(true)
    const target = first && first !== start ? first : start
    target.forceActiveFocus(Qt.ShortcutFocusReason)
  }

  function leaveKeyboard() {
    const back = keyboardReturn
    keyboardActive = false
    keyboardReturn = null
    // Focus parks on an item that is not a button, so the next Win+T or Narrator focus is a change again.
    parked.forceActiveFocus()
    if (back)
      back.activate()
  }

  onActiveFocusItemChanged: {
    if (activeFocusItem && activeFocusItem !== parked && !keyboardActive) {
      keyboardReturn = WindowList.activeWindow
      keyboardActive = true
    }
  }

  Connections {
    target: WindowList
    function onActiveWindowChanged() {
      if (root.keyboardActive && WindowList.activeWindow !== null) {
        root.keyboardActive = false
        root.keyboardReturn = null
        parked.forceActiveFocus()
      }
    }
  }

  // Whether something the taskbar opened is up, which keeps an auto-hiding taskbar shown.
  readonly property bool busy: keyboardActive || taskList.busy || startOpen || searchOpen || menu.visible
    || (trayLoader.item !== null && trayLoader.item.busy === true)
    || (clockLoader.item !== null && clockLoader.item.busy === true)
  property bool revealed: !autoHide

  anchors {
    bottom: true
    left: true
    right: true
  }

  // The taskbar's own height; the window is shorter while an auto-hiding taskbar is down.
  readonly property real barHeight: 48
  // An auto-hiding taskbar reserves nothing and lays out against the whole screen.
  exclusionMode: autoHide ? ExclusionMode.Ignore : ExclusionMode.Auto
  // A hidden taskbar is a line of 2 pixels at the edge, which brings it back when the pointer touches it. It slides
  // by growing its window from that line, with the top of the taskbar first, so the window never leaves its screen
  // for the one below.
  implicitHeight: barHeight - hiddenBy
  property real hiddenBy: revealed ? 0 : barHeight - 2
  Behavior on hiddenBy { NumberAnimation { duration: Theme.controlNormalAnimationDuration; easing.type: Easing.OutCubic } }

  color: "transparent"
  WindowEffects.backdrop: Preferences.material
  // Narrator names Explorer's taskbar this way.
  Accessible.name: "Taskbar"

  onAutoHideChanged: revealed = !autoHide
  onKeyboardActiveChanged: if (keyboardActive) revealed = true
  onBusyChanged: if (!busy) hideTimer.restart()

  Timer {
    id: hideTimer
    interval: 1000
    onTriggered: if (root.autoHide && !hover.hovered && !root.busy) root.revealed = false
  }

  Item {
    id: content
    width: parent.width
    height: root.barHeight

    // A right click where there is no button opens Taskbar settings, as on Windows 11.
    MouseArea {
      objectName: "taskbarBackground"
      anchors.fill: parent
      acceptedButtons: Qt.RightButton
      onClicked: mouse => menu.openAt(mouse.x)
    }

    // Tab and Shift+Tab move through Start, Search, the apps, the tray and the clock; so do Left and Right.
    Keys.onPressed: event => {
      // A Keys handler takes every key unless it says otherwise.
      event.accepted = false
      const focused = root.activeFocusItem
      if (event.key === Qt.Key_Escape && root.keyboardActive) {
        root.leaveKeyboard()
      } else if ((event.key === Qt.Key_Left || event.key === Qt.Key_Right) && focused && focused !== parked) {
        const next = focused.nextItemInFocusChain(event.key === Qt.Key_Right)
        if (next)
          next.forceActiveFocus(Qt.TabFocusReason)
      } else {
        return
      }
      event.accepted = true
    }

    Item {
      id: parked
    }

    HoverHandler {
      id: hover
      onHoveredChanged: {
        if (hovered)
          root.revealed = true
        else
          hideTimer.restart()
      }
    }

    // The thin line Windows 11 draws along the top of the taskbar.
    Rectangle {
      width: parent.width
      height: 1
      color: Theme.surfaceStrokeColorDefault
    }

    Row {
      id: middle
      objectName: "taskbarApps"
      // Start, Search and as many apps as fit before the tray area, wherever the group is while it slides.
      readonly property real fullWidth: taskList.x + Math.min(taskList.naturalWidth, right.x - 12 - 12 - taskList.x)
      // Centered apps move left rather than run under the tray area.
      x: root.centered ? Math.max(12, Math.min(Math.round((parent.width - fullWidth) / 2), right.x - fullWidth - 12)) : 12
      anchors.verticalCenter: parent.verticalCenter
      spacing: 4

      Behavior on x { NumberAnimation { duration: Theme.controlNormalAnimationDuration; easing.type: Easing.OutCubic } }

      TaskbarButton {
        id: start
        objectName: "startButton"
        label: "Start"
        checked: root.startOpen
        onInvoked: root.startRequested(start)
        windowsLogo: true
      }

      TaskbarButton {
        id: search
        objectName: "searchButton"
        label: "Search"
        glyph: ""
        checked: root.searchOpen
        onInvoked: root.searchRequested(search)
      }

      TaskList {
        id: taskList
        windows: WindowList.windows.values
        activeWindow: WindowList.activeWindow
        apps: Apps
        pinned: root.pins ? root.pins.ids : []
        screen: root.screen
        allScreens: root.appsOn === "all" || (root.appsOn === "main" && root.isMainScreen)
        showPins: root.appsOn === "all" || root.isMainScreen
        onPinRequested: id => root.pins.pin(id)
        onUnpinRequested: id => root.pins.unpin(id)
        // The focused app left. Start, or whichever app is first, is still there; the one that left is not.
        onKeyboardDisplaced: if (root.keyboardActive) root.focusApps()
        // Up to the tray area, after the margins and Start and Search.
        // Up to the tray area from where the group is now, so the apps never reach it, even while the group slides.
        maxWidth: right.x - 12 - middle.x - x
      }
    }

    Row {
      id: right
      objectName: "taskbarRight"
      anchors.right: parent.right
      anchors.rightMargin: 12
      anchors.verticalCenter: parent.verticalCenter
      spacing: 4

      Loader {
        id: trayLoader
        anchors.verticalCenter: parent.verticalCenter
        sourceComponent: root.trayArea
      }

      Loader {
        id: clockLoader
        anchors.verticalCenter: parent.verticalCenter
        sourceComponent: root.clockArea
      }
    }
  }

  TaskbarMenu {
    id: menu
    anchor.window: root
    onSettingsRequested: root.settingsRequested()
  }
}
