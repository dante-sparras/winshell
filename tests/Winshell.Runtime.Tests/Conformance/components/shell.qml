// File types, inline components, Component with createObject, Loader, Qt.createComponent and required properties.
import QtQuick

Item {
    id: root
    component Chip: QtObject { property string text: "chip" }

    property Chip chip: Chip { text: "inline" }
    property Badge badge: Badge {
        label: "file"
        onActivated: console.log("activated", label)
    }

    Component {
        id: squares
        QtObject {
            required property int n
            property int square: n * n
            Component.onCompleted: console.log("created", n, square)
            Component.onDestruction: console.log("destroyed", n)
        }
    }

    Component {
        id: labelComponent
        Text { text: "from a component" }
    }

    Loader {
        id: loader
        active: false
        sourceComponent: labelComponent
        onLoaded: console.log("loaded", item.text)
    }

    Loader {
        id: fileLoader
        source: "Badge.qml"
    }

    Component.onCompleted: {
        console.log(chip.text, badge.label, badge.upper)
        badge.activate()
        const made = squares.createObject(root, { n: 3 })
        made.n = 4
        console.log("square", made.square)
        made.destroy()
        console.log("loader", loader.item, loader.status === Loader.Null)
        loader.active = true
        console.log("loader", loader.status === Loader.Ready)
        loader.active = false
        console.log("loader", loader.item, loader.status === Loader.Null)
        console.log("file loader", fileLoader.status === Loader.Ready, fileLoader.item.upper)
        const dynamic = Qt.createComponent("Badge.qml")
        const made2 = dynamic.createObject(root, { label: "dynamic" })
        console.log("dynamic", made2.upper, dynamic.status === Component.Ready)
    }
}

// expect: inline file FILE
// expect: activated file
// expect: created 3 9
// expect: square 16
// expect: loader null true
// expect: loaded from a component
// expect: loader true
// expect: loader null true
// expect: file loader true BADGE
// expect: dynamic DYNAMIC true
// expect: destroyed 4
