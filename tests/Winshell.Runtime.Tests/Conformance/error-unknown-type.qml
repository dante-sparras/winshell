// A type that does not exist fails the load with the file and line.
import QtQml

QtObject {
    property QtObject child: Missing {}
}

// expect: ERROR scene: Failed to load configuration: error-unknown-type.qml[5:30]: Missing is not a type
