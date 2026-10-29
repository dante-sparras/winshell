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
}
