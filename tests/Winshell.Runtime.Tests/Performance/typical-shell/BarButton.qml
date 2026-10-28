import QtQuick

MouseArea {
    id: button
    property string label
    property bool active: false

    implicitWidth: Math.max(32, text.implicitWidth + 16)
    implicitHeight: 32
    hoverEnabled: true

    Rectangle {
        anchors.fill: parent
        radius: 4
        color: button.pressed ? "#33ffffff" : button.containsMouse ? "#22ffffff" : button.active ? "#18ffffff" : "transparent"
        border.width: button.active ? 1 : 0
        border.color: "#40ffffff"
        Behavior on color { ColorAnimation { duration: 83 } }
    }

    Text {
        id: text
        anchors.centerIn: parent
        text: button.label
        color: button.active ? "#ffffff" : "#c8ffffff"
        font.pixelSize: 12
        horizontalAlignment: Text.AlignHCenter
    }
}
