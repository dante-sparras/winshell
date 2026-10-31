import Winshell
import Winshell.Io
import QtQuick

// The apps pinned to the taskbar, kept in taskbar.json in the shell's data folder. Until you pin, unpin or
// reorder something, the taskbar has the apps Windows 11 pins: File Explorer, Microsoft Edge and the Microsoft Store.
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

  // Puts this pin where another pin is: before it when moving back, after it when moving on.
  function move(id, target) {
    const pins = adapter.pinned
    const order = pins.filter(pinned => pinned !== id)
    const at = pins.indexOf(target)
    if (!id || id === target || at < 0 || pins.indexOf(id) < 0)
      return
    order.splice(at, 0, id)
    adapter.pinned = order
  }

  FileView {
    path: root.path
    // A few bytes, read and written at once. A read still on its way could otherwise land after a pin and undo it,
    // and two quick changes could be written in the wrong order.
    blockLoading: true
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
