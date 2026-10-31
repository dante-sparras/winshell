import Winshell.Io
import QtQuick

// launcher.toml, the apps pinned to Start. [] pins nothing; without this file Start pins its defaults.
// Start and Winshell Settings both watch this file, so a change in either one shows up in the other.
FileView {
  id: root

  blockLoading: true
  blockWrites: true
  watchChanges: true
  printErrors: false
  onFileChanged: reload()

  property alias pins: adapter.pins
  property alias error: adapter.error

  TomlAdapter {
    id: adapter
    comments: ({ pins: "The apps pinned to Start, in order. [] pins nothing; without this file Start pins its defaults." })
    // null until the file has pins.
    property var pins: null
  }
}
