// A binding that throws reports the file and line, keeps the property's value, and runs again when it can.
import QtQml

QtObject {
    property QtObject source: null
    property int value: 7
    property int fromSource: source.value

    Component.onCompleted: {
        console.log("fromSource", fromSource)
        source = this
        console.log("fromSource", fromSource)
    }
}

// expect: WARN scene: error-in-binding.qml[7:30]: TypeError: Cannot read properties of null (reading 'value')
// expect: fromSource 0
// expect: fromSource 7
