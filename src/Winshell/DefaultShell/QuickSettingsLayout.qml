import Winshell.Io
import QtQuick

// quick-settings.toml, the tiles in order. Quick settings and Winshell Settings both watch this file.
FileView {
  id: root

  blockLoading: true
  blockWrites: true
  atomicWrites: true
  watchChanges: true
  onFileChanged: reload()

  property alias pinned: adapter.pinned

  TomlAdapter {
    id: adapter
    comments: ({ pinned: "The quick settings tiles, in order: wifi, bluetooth, airplaneMode, energySaver, powerMode, nightLight,\naccessibility. A tile left out is not shown until quick settings or Winshell Settings adds it back." })
    // null until the file has tiles.
    property var pinned: null
  }
}
