// JavaScript imports: a library shared by every importer, .import inside a script, and directory imports.
import QtQml
import "util.js" as Util
import "widgets"
import config.widgets as W

QtObject {
    property Label first: Label {}
    property W.Label second: W.Label {}

    Component.onCompleted: {
        console.log(Util.greet("ada"), Util.version)
        console.log(first.text, "|", second.text)
        console.log("calls", Util.calls)
        console.log("counter", first.counted, second.counted)
    }
}

// expect: hello ada 9 1.0
// expect: hello label 1 | hello label 4
// expect: calls 3
// expect: counter 1 1
