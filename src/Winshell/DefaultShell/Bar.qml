import Winshell
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls

// A bar along the top of the screen in the Windows 11 style: Acrylic behind it, Segoe UI Variable text.
PanelWindow {
  id: bar
  property var notificationListener: null

  anchors {
    top: true
    left: true
    right: true
  }

  implicitHeight: 40
  color: "transparent"
  WindowEffects.backdrop: WindowEffects.Acrylic

  // A bottom taskbar calls the same open, close, and toggle. The flyout flips when this bar is on the bottom edge.
  property bool launcherOpen: launcher.visible

  function openLauncher() { launcher.open() }
  function closeLauncher() { launcher.close() }
  function toggleLauncher() { launcher.toggle() }

  SystemClock {
    id: clock
    precision: SystemClock.Minutes
  }

  CalendarMonth { id: clockFormat; date: clock.date }

  RowLayout {
    anchors.fill: parent
    anchors.leftMargin: 8
    anchors.rightMargin: 8
    spacing: 4

    BarButton {
      id: startButton
      objectName: "startButton"
      icon: "\uE782"
      Accessible.role: Accessible.Button
      Accessible.name: "Start"
      onClicked: bar.toggleLauncher()
    }

    BarButton {
      icon: ""
      label: "Search"
      onClicked: Shell.execDetached(["explorer.exe", "search-ms:"])
    }

    Item { Layout.fillWidth: true }

    QuickSettingsButton {
      id: quickSettingsButton
      onClicked: quickSettings.visible = !quickSettings.visible
    }

    Button {
      id: clockButton
      text: clockFormat.timeLabel + "\n" + clockFormat.shortDateLabel
      font.pixelSize: 12
      padding: 0
      horizontalPadding: 10
      implicitHeight: 32
      flat: true
      Accessible.name: clockFormat.timeLabel + ", " + clockFormat.dateLabel
      Accessible.description: "Open notification center and calendar"
      onClicked: calendar.visible = !calendar.visible
    }
  }

  CalendarFlyout {
    id: calendar
    anchor.item: clockButton
    anchor.edges: Edges.Bottom | Edges.Right
    anchor.gravity: Edges.Bottom | Edges.Left
    anchor.margins.top: 8
    date: clock.date
    listener: bar.notificationListener
  }

  QuickSettings {
    id: quickSettings
    anchor.item: quickSettingsButton
    anchor.edges: Edges.Bottom | Edges.Right
    anchor.gravity: Edges.Bottom | Edges.Left
    anchor.margins.top: 8
  }

  Launcher {
    id: launcher
    anchor.item: startButton
    anchor.edges: bar.anchors.bottom ? (Edges.Top | Edges.Left) : (Edges.Bottom | Edges.Left)
    anchor.gravity: bar.anchors.bottom ? (Edges.Top | Edges.Left) : (Edges.Bottom | Edges.Left)
    anchor.margins.top: bar.anchors.bottom ? 0 : 8
    anchor.margins.bottom: bar.anchors.bottom ? 8 : 0
  }
}
