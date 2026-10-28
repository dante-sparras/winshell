// Enums, default and list properties, readonly properties, Behavior, and Qt.callLater.
pragma ComponentBehavior: Bound
import QtQuick

Item {
    id: root
    property int mode: Holder.Auto
    readonly property int fixed: 3
    property list<QtObject> things: [
        QtObject { objectName: "one" },
        QtObject { objectName: "two" }
    ]
    property Holder holder: Holder {
        QtObject { objectName: "first" }
        QtObject { objectName: "second" }
    }

    Item {
        id: mover
        x: 0
        Behavior on x { NumberAnimation { duration: 100 } }
    }

    Timer {
        interval: 500
        running: true
        onTriggered: console.log("moved to", mover.x)
    }

    Component.onCompleted: {
        console.log("mode", mode, Holder.Mode.On)
        console.log("things", things.length, things[1].objectName)
        console.log("held", holder.items.length, holder.items[0].objectName)
        Qt.callLater(() => console.log("called later"))
        console.log("before later")
        mover.x = 50
        console.log("moving from", mover.x)
        fixed = 4
    }
}

// expect: mode 6 5
// expect: things 2 two
// expect: held 2 first
// expect: before later
// expect: moving from 0
// expect: WARN scene: shell.qml[38:9]: TypeError: Cannot assign to read-only property "fixed"
// expect: called later
// expect: moved to 50
