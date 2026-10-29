import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// The clock hook opens one keyboard-grabbing surface. Placement is clamped to its monitor's work area.
PopupWindow {
  id: root
  property date date: new Date()
  property var listener: null
  property alias showWeekNumbers: calendar.showWeekNumbers
  implicitWidth: Math.min(384, screen ? screen.workArea.width : 384)
  implicitHeight: Math.min(780, screen ? screen.workArea.height - 16 : 780)
  color: Theme.isHighContrast ? Theme.solidBackgroundFillColorBase : "transparent"
  grabFocus: true
  WindowEffects.backdrop: WindowEffects.Acrylic
  WindowEffects.corners: WindowEffects.RoundCorners
  Accessible.name: "Notification center and calendar"

  function close() { visible = false; if (anchor.item) anchor.item.forceActiveFocus() }

  onVisibleChanged: if (visible) { calendar.month.reset(); closeButton.forceActiveFocus() }

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 16
    spacing: 12
    Keys.onEscapePressed: root.close()

    RowLayout {
      Layout.fillWidth: true
      Text { Layout.fillWidth: true; text: "Notification center"; font.weight: Font.DemiBold; color: Theme.textFillColorPrimary }
      Button { id: closeButton; text: "Close"; flat: true; onClicked: root.close() }
    }
    NotificationCenter { Layout.fillWidth: true; Layout.fillHeight: true; listener: root.listener }
    Rectangle { Layout.fillWidth: true; height: 1; color: Theme.controlStrokeColorDefault }
    // A second scroll view keeps the calendar reachable on small or highly scaled monitors.
    FocusScrollView {
      id: calendarScroll
      Layout.fillWidth: true
      Layout.preferredHeight: Math.min(360, root.height * 0.5)
      contentWidth: availableWidth
      CalendarView { id: calendar; width: calendarScroll.availableWidth; date: root.date; scrollView: calendarScroll }
    }
  }
}
