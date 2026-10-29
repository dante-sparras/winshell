import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Controls

// The taskbar's default clock area: the time and date, which open the notification center and calendar.
Item {
  id: root

  property var notificationListener: null
  // Whether the flyout is open, so an auto-hiding taskbar stays up.
  readonly property bool busy: calendar.visible

  // Win+N, as a click on the clock.
  function toggleCalendar() {
    calendar.visible = !calendar.visible
  }

  implicitWidth: clockButton.implicitWidth
  implicitHeight: 40

  SystemClock {
    id: clock
    precision: SystemClock.Minutes
  }

  CalendarMonth { id: clockFormat; date: clock.date }

  Button {
    id: clockButton
    anchors.verticalCenter: parent.verticalCenter
    text: clockFormat.timeLabel + "\n" + clockFormat.shortDateLabel
    font: Theme.typography.caption
    padding: 0
    horizontalPadding: 10
    implicitHeight: 40
    flat: true
    Accessible.name: clockFormat.timeLabel + ", " + clockFormat.dateLabel
    Accessible.description: "Open notification center and calendar"
    onClicked: root.toggleCalendar()
  }

  CalendarFlyout {
    id: calendar
    anchor.item: clockButton
    anchor.edges: Edges.Top | Edges.Right
    anchor.gravity: Edges.Top | Edges.Left
    anchor.margins.top: 8
    date: clock.date
    listener: root.notificationListener
  }
}
