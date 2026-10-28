// States set by name or by when, PropertyChanges by target or by id that are undone on leaving, and the
// transition that matches from and to, animating only the properties it names.
import QtQuick

Item {
    id: box
    property bool wide: false
    width: 10
    height: 10
    opacity: 1

    Item { id: label; width: 1 }

    states: [
        State {
            name: "wide"
            when: box.wide
            PropertyChanges { target: box; width: 100 }
            PropertyChanges { label.width: 50 }
        },
        State {
            name: "faded"
            PropertyChanges { target: box; opacity: 0.5; height: 20 }
        }
    ]
    transitions: [
        Transition {
            from: "wide"
            to: "faded"
            NumberAnimation { properties: "opacity"; duration: 5000 }
        },
        Transition {
            to: "faded"
            NumberAnimation { properties: "opacity"; duration: 200 }
        }
    ]
    onStateChanged: console.log("state", JSON.stringify(state))

    Timer {
        interval: 1000
        running: true
        onTriggered: {
            console.log("opacity after the transition", box.opacity)
            box.state = ""
            console.log("opacity restored", box.opacity, "height", box.height)
        }
    }

    Component.onCompleted: {
        wide = true
        console.log("width", width, "label", label.width)
        wide = false
        console.log("width", width, "label", label.width)
        state = "faded"
        console.log("opacity when the transition starts", opacity, "height", height)
    }
}

// expect: state "wide"
// expect: width 100 label 50
// expect: state ""
// expect: width 10 label 1
// expect: state "faded"
// expect: opacity when the transition starts 1 height 20
// expect: opacity after the transition 0.5
// expect: state ""
// expect: opacity restored 1 height 10
