import Winshell.Theme
import QtQuick
import QtQuick.Controls
import "launcher.js" as Launcher
import "settings.js" as Settings

// Quick settings: the tiles in order, to move, remove and add back. They are quick settings' own file, which it
// follows as it changes, the same as its edit mode.
Column {
  id: root

  property string layoutPath: ""

  property var keys: Settings.tiles.map(t => t.key)
  readonly property var pinned: keys.map(key => Settings.tiles.find(t => t.key === key)).filter(t => t !== undefined)
  readonly property var unpinned: Settings.tiles.filter(t => !keys.includes(t.key))

  spacing: 4

  function read() {
    if (Array.isArray(file.pinned))
      keys = file.pinned.filter(k => typeof k === "string")
  }

  function write(next) {
    keys = next
    if (layoutPath === "")
      return
    file.pinned = next
    file.writeAdapter()
  }

  // Moves a tile past the one next to it in the list; keys quick settings does not know keep their place.
  function reorder(key, offset) {
    const next = Launcher.reorder(keys, pinned.map(t => t.key), key, offset)
    if (next)
      write(next)
  }

  QuickSettingsLayout {
    id: file
    path: root.layoutPath
    printErrors: false
    onLoaded: root.read()
  }

  SettingsCard { key: "quickSettingsTiles" }

  SettingsOrderList {
    id: tiles
    objectName: "tileList"
    width: parent.width
    items: root.pinned
    removeLabel: "Remove"
    removeGlyph: "\uE738"
    emptyText: "Quick settings shows no tiles"
    onMoveRequested: (key, offset) => root.reorder(key, offset)
    onRemoveRequested: key => root.write(root.keys.filter(k => k !== key))
  }

  SettingsHeader {
    visible: root.unpinned.length > 0
    text: "Add tiles"
  }

  Repeater {
    model: root.unpinned

    SettingsCard {
      id: addCard
      required property var modelData
      objectName: "add:" + modelData.key
      key: "quickSettingsTiles"
      title: modelData.title
      description: ""
      glyph: modelData.glyph
      focusTarget: add

      Button {
        id: add
        objectName: "add"
        text: "Add"
        Accessible.name: "Add " + addCard.modelData.title
        onClicked: {
          const index = root.unpinned.indexOf(addCard.modelData)
          root.write(root.keys.concat([addCard.modelData.key]))
          // The keyboard stays among the tiles left to add, or goes to the one just added.
          Qt.callLater(() => {
            const next = root.unpinned[Math.min(index, root.unpinned.length - 1)]
            const target = next ? root.children.find(c => c.objectName === "add:" + next.key) : null
            if (target)
              target.focusSetting()
            else
              tiles.focusButton(addCard.modelData.key, "remove")
          })
        }
        onActiveFocusChanged: if (activeFocus) addCard.reveal()
      }
    }
  }
}
