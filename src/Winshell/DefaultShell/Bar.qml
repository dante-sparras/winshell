import Winshell
import QtQuick
import QtQuick.Layouts

// A bar along the top of the screen in the Windows 11 style: Acrylic behind it, Segoe UI Variable text.
PanelWindow {
  id: bar

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

    BarButton {
      id: clockButton
      label: Qt.formatDateTime(clock.date, "HH:mm") + "\n" + Qt.formatDateTime(clock.date, "d.M.yyyy")
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
  }

  QuickSettings {
    id: quickSettings
    anchor.item: quickSettingsButton
    anchor.edges: Edges.Bottom | Edges.Right
    anchor.gravity: Edges.Bottom | Edges.Left
    anchor.margins.top: 8
  }
}
