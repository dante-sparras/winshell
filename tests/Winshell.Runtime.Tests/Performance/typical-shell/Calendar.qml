import Quickshell
import QtQuick
import QtQuick.Layouts
import "dates.js" as Dates

PopupWindow {
    id: root
    property date date: new Date()
    property int monthOffset: 0
    readonly property date shown: new Date(date.getFullYear(), date.getMonth() + monthOffset, 1)

    implicitWidth: 320
    implicitHeight: 360
    color: "transparent"

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 16

        Text {
            text: Dates.month(root.shown)
            font.pixelSize: 14
            color: "#ffffff"
        }

        GridLayout {
            columns: 7
            Repeater {
                model: 42
                Text {
                    required property int index
                    readonly property date day: Dates.cell(root.shown, index)
                    text: day.getDate()
                    opacity: day.getMonth() === root.shown.getMonth() ? 1 : 0.4
                    color: Dates.sameDay(day, root.date) ? "#60cdff" : "#ffffff"
                    horizontalAlignment: Text.AlignHCenter
                    Layout.fillWidth: true
                }
            }
        }
    }
}
