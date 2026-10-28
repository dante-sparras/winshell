// A syntax error fails the load with the file and line.
import QtQml

QtObject {
    property int a: 1
    property int b: 2 +
}

// expect: ERROR scene: Failed to load configuration: error-syntax.qml[7:1]: SyntaxError: Unexpected token ')'
