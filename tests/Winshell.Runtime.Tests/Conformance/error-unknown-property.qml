// Assigning a property that does not exist fails the load with the file and line.
import QtQml

QtObject {
    colour: "red"
}

// expect: ERROR scene: Failed to load configuration: error-unknown-property.qml[5:5]: Cannot assign to non-existent property "colour"
