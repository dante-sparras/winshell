// A shell shaped like a typical bar config: a bar per screen with workspaces, a tray, a clock,
// a volume slider and a calendar popup. The Jint budget loads it.
import Winshell
import QtQuick

ShellRoot {
    id: root
    property var screens: [{ name: "DISPLAY1" }, { name: "DISPLAY2" }, { name: "DISPLAY3" }]
    property int activeWorkspace: 1

    ListModel {
        id: tray
        ListElement { name: "Network"; icon: ""; urgent: false }
        ListElement { name: "Volume"; icon: ""; urgent: false }
        ListElement { name: "Battery"; icon: ""; urgent: false }
        ListElement { name: "Bluetooth"; icon: ""; urgent: false }
        ListElement { name: "Updates"; icon: ""; urgent: true }
        ListElement { name: "Chat"; icon: ""; urgent: false }
        ListElement { name: "Mail"; icon: ""; urgent: false }
        ListElement { name: "Cloud"; icon: ""; urgent: false }
    }

    Variants {
        model: root.screens

        Bar {
            required property var modelData
            screenName: modelData.name
            workspace: root.activeWorkspace
            trayModel: tray
        }
    }
}
