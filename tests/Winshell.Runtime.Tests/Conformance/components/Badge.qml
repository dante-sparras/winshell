import QtQml

QtObject {
    id: badge
    property string label: "badge"
    readonly property string upper: label.toUpperCase()
    signal activated()
    function activate() { activated() }
}
