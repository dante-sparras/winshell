// An error thrown inside an imported script names the script's file and line.
import QtQml
import "helpers.js" as Helpers

QtObject {
    Component.onCompleted: Helpers.fail("boom")
}

// expect: WARN scene: helpers.js[3:11]: Error: boom
