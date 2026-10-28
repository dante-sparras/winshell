import QtQml

QtObject {
    property int value: thrower()
    function thrower() { throw new Error("thrown while loading") }
}
