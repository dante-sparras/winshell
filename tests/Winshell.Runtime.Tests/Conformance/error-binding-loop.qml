// A binding loop is reported with the property and where it was written, and does not hang.
import QtQml

QtObject {
    property int a: b + 1
    property int b: a + 1
}

// expect: WARN scene: error-binding-loop.qml[6:5]: QML QtObject: Binding loop detected for property "b"
