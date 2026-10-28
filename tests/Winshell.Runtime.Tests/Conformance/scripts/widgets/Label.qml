import QtQml
import "../util.js" as Util
import "../counter.js" as Counter

QtObject {
    property string text: Util.greet("label")
    property int counted: Counter.next()
}
