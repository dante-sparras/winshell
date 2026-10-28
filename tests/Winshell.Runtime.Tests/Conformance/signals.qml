// Declared signals, handlers, connect and disconnect, signal to signal, Connections, and change signals.
import QtQml

QtObject {
    id: root
    signal greeted(string name, int times)
    signal relayed(string name, int times)
    property int count: 0
    onCountChanged: console.log("countChanged", count)
    onGreeted: (name, times) => console.log("arrow handler", name, times)

    property QtObject child: QtObject {
        id: child
        signal ping(int value)
        onPing: console.log("injected parameter", value)
    }
    property Connections connections: Connections {
        id: connections
        target: child
        function onPing(value) { console.log("Connections", value) }
    }

    Component.onCompleted: {
        greeted.connect(relayed)
        relayed.connect((name, times) => console.log("relayed", name, times))
        const handler = name => console.log("connected function", name)
        greeted.connect(handler)
        greeted("ada", 2)
        greeted.disconnect(handler)
        greeted("bob", 1)
        child.ping(7)
        connections.enabled = false
        child.ping(8)
        connections.enabled = true
        connections.target = null
        child.ping(9)
        count = 1
        count = 1
    }
}

// expect: arrow handler ada 2
// expect: relayed ada 2
// expect: connected function ada
// expect: arrow handler bob 1
// expect: relayed bob 1
// expect: injected parameter 7
// expect: Connections 7
// expect: injected parameter 8
// expect: injected parameter 9
// expect: countChanged 1
