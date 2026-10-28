// Bindings follow what they read, an assignment breaks them, and Qt.binding makes a new one.
import QtQml

QtObject {
    id: root
    property int a: 1
    property int b: a + 1
    property int c: { const twice = b * 2; twice + 1 }
    property string label: `a=${a} b=${b}`
    property QtObject other: QtObject {
        id: other
        property int value: root.a * 10
    }
    property int fromOther: other.value + 1
    property bool flag: false
    property int conditional: flag ? a : -1
    property var list: [1, 2, 3]
    property int listLength: list.length
    property int viaFunction: doubled()
    function doubled(): int { return a * 2 }

    Component.onCompleted: {
        console.log("b", b, "c", c, label, "fromOther", fromOther, "viaFunction", viaFunction)
        a = 5
        console.log("b", b, "c", c, label, "fromOther", fromOther, "viaFunction", viaFunction)
        b = 100
        a = 6
        console.log("b after assignment", b, "c", c)
        b = Qt.binding(() => a * 3)
        console.log("b rebound", b, "c", c)
        a = 7
        console.log("b follows", b, "c", c)
        console.log("conditional", conditional)
        flag = true
        console.log("conditional", conditional)
        list = [1, 2]
        console.log("listLength", listLength)
    }
}

// expect: b 2 c 5 a=1 b=2 fromOther 11 viaFunction 2
// expect: b 6 c 13 a=5 b=6 fromOther 51 viaFunction 10
// expect: b after assignment 100 c 201
// expect: b rebound 18 c 37
// expect: b follows 21 c 43
// expect: conditional -1
// expect: conditional 7
// expect: listLength 2
