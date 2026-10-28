import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Layouts

// The month view Windows 11 shows from the clock, on an Acrylic flyout.
PopupWindow {
  id: root

  property date date: new Date()
  property int monthOffset: 0
  readonly property date shown: new Date(date.getFullYear(), date.getMonth() + monthOffset, 1)

  implicitWidth: 336
  implicitHeight: 400
  color: "transparent"
  grabFocus: true
  WindowEffects.backdrop: WindowEffects.Acrylic
  WindowEffects.corners: WindowEffects.RoundCorners

  onVisibleChanged: if (visible) monthOffset = 0

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 16
    spacing: 12

    Text {
      text: Qt.formatDateTime(root.date, "dddd, MMMM d")
      font.pixelSize: 14
      font.weight: Font.DemiBold
      color: Theme.textFillColorPrimary
    }

    RowLayout {
      Layout.fillWidth: true

      Text {
        Layout.fillWidth: true
        text: Qt.formatDateTime(root.shown, "MMMM yyyy")
        font.pixelSize: 14
        font.weight: Font.DemiBold
        color: Theme.textFillColorPrimary
      }

      MonthArrow { glyph: ""; onClicked: root.monthOffset-- }
      MonthArrow { glyph: ""; onClicked: root.monthOffset++ }
    }

    Grid {
      columns: 7
      spacing: 4

      Repeater {
        model: ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"]

        Text {
          required property string modelData
          width: 40
          height: 24
          text: modelData
          horizontalAlignment: Text.AlignHCenter
          verticalAlignment: Text.AlignVCenter
          font.pixelSize: 12
          color: Theme.textFillColorSecondary
        }
      }

      Repeater {
        // Six weeks starting on the Monday before the first of the month.
        model: 42

        Rectangle {
          required property int index
          readonly property int lead: (root.shown.getDay() + 6) % 7
          readonly property date day: new Date(root.shown.getFullYear(), root.shown.getMonth(), index - lead + 1)
          readonly property bool inMonth: day.getMonth() === root.shown.getMonth()
          readonly property bool today: day.toDateString() === root.date.toDateString()

          width: 40
          height: 40
          radius: 20
          color: today ? Theme.accentFillColorDefault : dayMouse.containsMouse ? Theme.subtleFillColorSecondary : "transparent"

          Text {
            anchors.centerIn: parent
            text: parent.day.getDate()
            font.pixelSize: 13
            color: parent.today ? Theme.textOnAccentFillColorPrimary : parent.inMonth ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
          }

          MouseArea {
            id: dayMouse
            anchors.fill: parent
            hoverEnabled: true
          }
        }
      }
    }
  }

  component MonthArrow: MouseArea {
    id: arrow
    property string glyph
    implicitWidth: 32
    implicitHeight: 32
    hoverEnabled: true

    Rectangle {
      anchors.fill: parent
      radius: 4
      color: arrow.pressed ? Theme.subtleFillColorTertiary : arrow.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
    }

    Text {
      anchors.centerIn: parent
      text: arrow.glyph
      font.family: Theme.iconFontFamily
      font.pixelSize: 12
      color: Theme.textFillColorPrimary
    }
  }
}
