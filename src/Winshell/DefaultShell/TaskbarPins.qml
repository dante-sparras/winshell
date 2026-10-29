import Winshell
import Winshell.Io
import QtQuick

// The apps pinned to the taskbar, kept in taskbar.json in the shell's data folder. Until you pin or unpin
// something, the taskbar has the apps Windows 11 pins: File Explorer, Microsoft Edge and the Microsoft Store.
Scope {
  id: root

  // App ids from the app index, or the paths of programs it does not have.
  readonly property var ids: adapter.pinned
  // Where the pins are kept. Without a data folder they are not kept at all.
  property string path: Shell.dataDir ? Shell.dataDir + "/taskbar.json" : ""

  function pin(id) {
    if (id && adapter.pinned.indexOf(id) < 0)
      adapter.pinned = adapter.pinned.concat([id])
  }

  function unpin(id) {
    adapter.pinned = adapter.pinned.filter(pinned => pinned !== id)
  }

  FileView {
    path: root.path
    // A few bytes, written at once: two quick changes cannot then land in the wrong order.
    blockWrites: true
    watchChanges: true
    onFileChanged: reload()
    onAdapterUpdated: if (root.path) writeAdapter()

    JsonAdapter {
      id: adapter
      property var pinned: ["Microsoft.Windows.Explorer", "MSEdge", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"]
    }
  }
}
