import Winshell
import Winshell.Theme
import QtQuick
import "launcher.js" as Launcher

// Start > Pinned apps: the pins in Start's order, to move or unpin. They are Start's own file, launcher.toml next
// to the shell, which Start follows as it changes. Until something is pinned or moved, Start has its default pins.
Column {
  id: root

  property var apps: []
  property string pinsPath: ""
  property bool showIcons: true

  property var ids: []
  property bool known: false
  readonly property var shownIds: known ? ids : Launcher.defaultPins(apps)
  readonly property var pinned: Launcher.pinned(apps, shownIds)

  spacing: 4

  function read() {
    known = Array.isArray(file.pins)
    ids = known ? file.pins.slice() : []
  }

  function write(next) {
    ids = next
    known = true
    if (pinsPath === "")
      return
    file.pins = next
    file.writeAdapter()
  }

  // Moves a pin past the pin next to it in Start. Pins Start cannot show keep their place.
  function reorder(id, offset) {
    const next = Launcher.reorder(shownIds, pinned.map(app => app.id), id, offset)
    if (next)
      write(next)
  }

  function unpin(id) {
    write(shownIds.filter(k => !Launcher.sameId(k, id)))
  }

  StartPins {
    id: file
    path: root.pinsPath
    onLoaded: root.read()
    // A deleted file brings Start's default pins back, as the next Start does.
    onLoadFailed: {
      file.pins = null
      root.read()
    }
  }

  SettingsHeader {
    topPadding: 0
    text: "Pinned apps"
  }

  Text {
    width: parent.width
    bottomPadding: 8
    text: "To pin an app, right-click it in Start and choose Pin to Start."
    wrapMode: Text.Wrap
    font: Theme.typography.body
    color: Theme.textFillColorSecondary
  }

  SettingsOrderList {
    objectName: "pinList"
    width: parent.width
    items: root.pinned.map(app => ({ key: app.id, title: app.name, icon: root.showIcons ? app.icon ?? "" : "" }))
    removeLabel: "Unpin"
    removeGlyph: "\uE77A"
    emptyText: "Nothing pinned"
    onMoveRequested: (key, offset) => root.reorder(key, offset)
    onRemoveRequested: key => root.unpin(key)
  }
}
