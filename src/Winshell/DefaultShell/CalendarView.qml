import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// Kept separate from the window so the real surface can be laid out and tested without an HWND.
ColumnLayout {
  id: root
  property date date: new Date()
  property bool showWeekNumbers: Preferences.calendarWeekNumbers
  property string localeName: ""
  property var scrollView: null
  property alias month: month
  spacing: 8
  implicitHeight: 360

  CalendarMonth { id: month; date: root.date; localeName: root.localeName }

  Text {
    Layout.fillWidth: true
    text: month.dateLabel
    font.pixelSize: 18
    font.weight: Font.DemiBold
    color: Theme.textFillColorPrimary
    wrapMode: Text.Wrap
  }

  RowLayout {
    Layout.fillWidth: true
    Text {
      Layout.fillWidth: true
      text: month.monthLabel
      font.pixelSize: 14
      font.weight: Font.DemiBold
      color: Theme.textFillColorPrimary
    }
    Button { text: "Today"; flat: true; onClicked: month.reset(); onActiveFocusChanged: if (activeFocus && root.scrollView) root.scrollView.reveal(this) }
    Button { text: ""; font.family: Theme.iconFontFamily; implicitWidth: 32; padding: 0; flat: true; Accessible.name: "Previous month"; onClicked: month.monthOffset--; onActiveFocusChanged: if (activeFocus && root.scrollView) root.scrollView.reveal(this) }
    Button { text: ""; font.family: Theme.iconFontFamily; implicitWidth: 32; padding: 0; flat: true; Accessible.name: "Next month"; onClicked: month.monthOffset++; onActiveFocusChanged: if (activeFocus && root.scrollView) root.scrollView.reveal(this) }
  }

  Row {
    Layout.fillWidth: true
    Item { width: root.showWeekNumbers ? 28 : 0; height: 24 }
    Repeater {
      model: month.weekdayLabels
      Text {
        required property string modelData
        width: (root.width - (root.showWeekNumbers ? 28 : 0)) / 7
        height: 24
        text: modelData
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
        font.pixelSize: 12
        color: Theme.textFillColorSecondary
      }
    }
  }

  Row {
    Layout.fillWidth: true
    Column {
      visible: root.showWeekNumbers
      width: root.showWeekNumbers ? 28 : 0
      Repeater {
        model: month.weekNumbers
        Text {
          required property int modelData
          width: 28
          height: 36
          text: modelData
          Accessible.name: "Week " + modelData
          horizontalAlignment: Text.AlignHCenter
          verticalAlignment: Text.AlignVCenter
          font.pixelSize: 12
          color: Theme.textFillColorSecondary
        }
      }
    }
    Grid {
      columns: 7
      Repeater {
        model: month.days
        RoundButton {
          required property var modelData
          required property int index
          width: (root.width - (root.showWeekNumbers ? 28 : 0)) / 7
          height: 36
          padding: 0
          text: modelData.label
          flat: !modelData.isToday
          highlighted: modelData.isToday
          checked: modelData.isSelected
          contentItem: Text {
            text: modelData.label
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
            font.pixelSize: 14
            color: modelData.isToday ? Theme.textOnAccentFillColorPrimary : modelData.inMonth ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
          }
          Accessible.name: modelData.fullDate
          Accessible.description: (modelData.isToday ? "Today. " : "") + (modelData.isSelected ? "Selected. " : "") + (modelData.inMonth ? "" : "Adjacent month.")
          onClicked: month.selectDay(index)
          onActiveFocusChanged: if (activeFocus && root.scrollView) root.scrollView.reveal(this)
        }
      }
    }
  }

  CheckBox {
    text: "Week numbers"
    checked: root.showWeekNumbers
    onToggled: Preferences.calendarWeekNumbers = checked
    onActiveFocusChanged: if (activeFocus && root.scrollView) root.scrollView.reveal(this)
  }
}
