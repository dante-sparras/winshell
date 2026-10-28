// A Loader whose file is missing or broken reports it, has the Error status, and the shell keeps running.
import QtQuick

Item {
    Loader {
        id: missing
        source: "Missing.qml"
    }
    Loader {
        id: broken
        source: "Broken.qml"
    }
    Loader {
        id: throwing
        source: "Throwing.qml"
    }

    Component.onCompleted: {
        console.log("missing", missing.status === Loader.Error, missing.item)
        console.log("broken", broken.status === Loader.Error, broken.item)
        console.log("throwing", throwing.status === Loader.Ready, throwing.item.value)
    }
}

// expect: WARN scene: Missing.qml: No such file or directory
// expect: WARN scene: Broken.qml[5:1]: Expected a value
// expect: WARN scene: Throwing.qml[5:25]: Error: thrown while loading
// expect: missing true null
// expect: broken true null
// expect: throwing true 0
