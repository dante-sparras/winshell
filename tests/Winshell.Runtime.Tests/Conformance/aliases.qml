// A property alias reads and writes its target, follows its changes, and can alias an object or another alias.
import QtQml

QtObject {
    id: root
    property QtObject inner: QtObject {
        id: inner
        property int value: 1
        property QtObject deep: QtObject {
            id: deep
            property string name: "deep"
        }
    }
    property alias value: inner.value
    property alias innerObject: inner
    property alias deepName: deep.name
    property alias valueAgain: root.value
    onValueChanged: console.log("valueChanged", value)

    Component.onCompleted: {
        console.log("value", value, "again", valueAgain, "deepName", deepName)
        inner.value = 2
        value = 3
        console.log("inner.value", inner.value, "again", valueAgain)
        valueAgain = 4
        console.log("inner.value", inner.value)
        console.log("innerObject is inner", innerObject === inner)
        deepName = "renamed"
        console.log("deep.name", deep.name)
    }
}

// expect: value 1 again 1 deepName deep
// expect: valueChanged 2
// expect: valueChanged 3
// expect: inner.value 3 again 3
// expect: valueChanged 4
// expect: inner.value 4
// expect: innerObject is inner true
// expect: deep.name renamed
