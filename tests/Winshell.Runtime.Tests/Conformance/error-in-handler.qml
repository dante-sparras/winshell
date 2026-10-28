// A handler that throws reports the file and line of the statement that threw, and later handlers still run.
import QtQml

QtObject {
    signal poked()
    onPoked: {
        console.log("before")
        missing.call()
        console.log("never")
    }

    Component.onCompleted: {
        poked()
        console.log("after")
        undefinedFunction()
    }
}

// expect: before
// expect: WARN scene: error-in-handler.qml[8:9]: ReferenceError: missing is not defined
// expect: after
// expect: WARN scene: error-in-handler.qml[15:9]: ReferenceError: undefinedFunction is not defined
