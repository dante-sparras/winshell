import Quickshell
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "dates.js" as Dates

PanelWindow {
    id: bar
    property string screenName
    property int workspace
    property var trayModel
    property real volume: 0.4

    anchors { top: true; left: true; right: true }
    implicitHeight: 40
    color: "transparent"

    SystemClock { id: clock; precision: SystemClock.Seconds }

    RowLayout {
        anchors.fill: parent
        anchors.margins: 4
        spacing: 4

        Repeater {
            model: 10
            BarButton {
                required property int index
                label: (index + 1).toString()
                active: bar.workspace === index + 1
                onClicked: bar.workspace = index + 1
            }
        }

        Item { Layout.fillWidth: true }

        Text {
            text: bar.screenName + " · " + Math.round(bar.volume * 100) + "%"
            color: "#ffffff"
            font.pixelSize: 12
        }

        Slider {
            Layout.preferredWidth: 120
            from: 0
            to: 1
            value: bar.volume
            onMoved: bar.volume = value
        }

        Repeater {
            model: bar.trayModel
            BarButton {
                required property string name
                required property string icon
                required property bool urgent
                label: icon
                active: urgent
                ToolTip.text: name
            }
        }

        BarButton {
            id: clockButton
            label: Dates.time(clock.date) + "\n" + Dates.day(clock.date)
            onClicked: calendar.visible = !calendar.visible
        }
    }

    Calendar {
        id: calendar
        anchor.item: clockButton
        date: clock.date
    }
}
