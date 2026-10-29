import Winshell.Io
import Winshell.Theme
import Winshell.Network
import Winshell.Bluetooth
import Winshell.Power
import Winshell.Audio
import Winshell.Media
import QtQuick
import QtQuick.Controls

// What quick settings shows: the media playing, the tiles, the volume and brightness sliders, and a footer with the
// battery. A tile whose setting Winshell cannot change opens its page in Settings instead; values only change when
// the user moves a slider or picks a power mode or an output. In edit mode the user unpins, adds and reorders tiles,
// and the order is saved to layoutPath.
Item {
  id: root

  // The page shown instead of the tiles, "powerMode", "output" or "add", or "" for the tiles.
  property string page: ""
  property bool editing: false
  // The TOML file the pinned tiles are kept in. Empty keeps them for this run only.
  property string layoutPath: ""
  // The keys of the pinned tiles, in order. A pinned tile the PC has no hardware for keeps its place, hidden.
  property var pinned: ["wifi", "bluetooth", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"]
  readonly property var mediaSession: Media.sessions.values.find(s => s.isPlaying) ?? Media.sessions.values[0] ?? null
  readonly property var outputs: Audio.nodes.values.filter(n => n.isPlayback && !n.isSession)
  readonly property var tiles: [wifiTile, bluetoothTile, airplaneTile, energySaverTile, powerModeTile, nightLightTile, accessibilityTile]
  // A parent that sets pinned leaves it null while the parent is made.
  readonly property var shownTiles: (pinned ?? []).map(key => tiles.find(t => t.key === key)).filter(t => t !== undefined && t.available)
  readonly property var unpinnedTiles: tiles.filter(t => t.available && !(pinned ?? []).includes(t.key))
  readonly property var brightnessMonitor: Brightness.monitors.values.find(m => m.connection === BrightnessConnection.Internal) ?? null

  signal closeRequested()

  function openSettings(uri) {
    Qt.openUrlExternally(uri)
    closeRequested()
  }

  function activate(tile) {
    if (tile.page !== "")
      page = tile.page
    else
      openSettings(tile.settingsUri)
  }

  function setPinned(keys) {
    pinned = keys
    layout.pinned = keys
    layoutFile.writeAdapter()
  }

  // Puts the tile with this key where the tile with another key is, before it when moving back, after it when moving on.
  function move(key, target) {
    const keys = pinned.filter(k => k !== key)
    const at = pinned.indexOf(target)
    if (key === target || at < 0)
      return
    keys.splice(at, 0, key)
    setPinned(keys)
  }

  // Moves a tile by an offset among the tiles shown, and keeps the keyboard on it.
  function moveBy(key, offset) {
    const from = shownTiles.findIndex(t => t.key === key)
    const to = Math.max(0, Math.min(shownTiles.length - 1, from + offset))
    move(key, shownTiles[to].key)
    Qt.callLater(() => focusTile(key))
  }

  // Unpins a tile. From the keyboard, the focus goes to the tile that takes its place.
  function unpin(key, fromKeyboard) {
    const at = shownTiles.findIndex(t => t.key === key)
    setPinned(pinned.filter(k => k !== key))
    if (fromKeyboard && shownTiles.length > 0)
      Qt.callLater(() => focusTile(shownTiles[Math.min(at, shownTiles.length - 1)].key))
  }

  function focusTile(key) {
    const tile = grid.children.find(c => c.objectName === "tile:" + key)
    if (tile)
      tile.focusButton()
  }

  function restore() {
    if (Array.isArray(layout.pinned))
      pinned = layout.pinned.filter(k => typeof k === "string")
  }

  implicitWidth: 360
  implicitHeight: body.implicitHeight + footer.height
  focus: true
  Keys.onEscapePressed: page !== "" ? page = "" : editing ? editing = false : closeRequested()

  FileView {
    id: layoutFile
    path: root.layoutPath
    blockLoading: true
    blockWrites: true
    atomicWrites: true
    // Winshell Settings changes the tiles in the same file.
    watchChanges: true
    onFileChanged: reload()
    onLoaded: root.restore()

    TomlAdapter {
      id: layout
      comments: ({ pinned: "The quick settings tiles, in order: wifi, bluetooth, airplaneMode, energySaver, powerMode, nightLight,\naccessibility. A tile left out is not shown until quick settings or Winshell Settings adds it back." })
      // null until the file has tiles.
      property var pinned: null
    }
  }

  component TileSpec: QtObject {
    property string key
    property string glyph
    // The tile's name, for the add page and unpinning. The label under the tile can show more, such as a network.
    property string title
    property string label: title
    property string description
    property bool on
    property bool toggle: true
    property bool available: true
    property string settingsUri
    property string page: ""
  }

  TileSpec {
    id: wifiTile
    key: "wifi"
    title: "Wi-Fi"
    glyph: ""
    label: SystemStatus.wifiNetwork ? SystemStatus.wifiNetwork.name : title
    description: "Opens Wi-Fi settings"
    on: Networking.wifiEnabled
    available: SystemStatus.wifi !== null || Networking.wifiState !== WifiRadioState.Unknown
    settingsUri: "ms-settings:network-wifi"
  }

  TileSpec {
    id: bluetoothTile
    readonly property var connected: Bluetooth.devices.values.find(d => d.connected) ?? null
    key: "bluetooth"
    title: "Bluetooth"
    glyph: ""
    label: connected ? connected.name : title
    description: "Opens Bluetooth settings"
    on: Bluetooth.defaultAdapter ? Bluetooth.defaultAdapter.enabled : false
    available: Bluetooth.defaultAdapter !== null
    settingsUri: "ms-settings:bluetooth"
  }

  TileSpec {
    id: airplaneTile
    key: "airplaneMode"
    title: "Airplane mode"
    glyph: ""
    description: "Opens airplane mode settings"
    on: Networking.airplaneMode === AirplaneModeState.On
    settingsUri: "ms-settings:network-airplanemode"
  }

  TileSpec {
    id: energySaverTile
    key: "energySaver"
    title: "Energy saver"
    glyph: ""
    description: "Opens energy saver settings"
    on: SystemStatus.energySaver
    settingsUri: "ms-settings:batterysaver"
  }

  TileSpec {
    id: powerModeTile
    readonly property var names: ["Best power efficiency", "Balanced", "Best performance"]
    key: "powerMode"
    title: "Power mode"
    glyph: ""
    description: names[PowerMode.mode] ?? ""
    toggle: false
    available: PowerMode.available
    page: "powerMode"
  }

  TileSpec {
    id: nightLightTile
    key: "nightLight"
    title: "Night light"
    glyph: ""
    description: "Opens night light settings"
    // Windows has no API that reads night light, so the tile does not pretend to know.
    toggle: false
    settingsUri: "ms-settings:nightlight"
  }

  TileSpec {
    id: accessibilityTile
    key: "accessibility"
    title: "Accessibility"
    glyph: ""
    description: "Opens accessibility settings"
    toggle: false
    settingsUri: "ms-settings:easeofaccess"
  }

  Item {
    id: body
    width: parent.width
    implicitHeight: main.implicitHeight

    Column {
      id: main
      objectName: "mainPage"
      visible: root.page === ""
      x: 24
      width: parent.width - 48
      topPadding: 24
      bottomPadding: 20
      spacing: 20

      MediaCard {
        objectName: "media"
        session: root.mediaSession
        visible: session !== null
      }

      Grid {
        id: grid
        objectName: "tiles"
        // The tile being dragged and the place it would land, as indexes into shownTiles, or -1.
        property int dragIndex: -1
        property int dropIndex: -1
        columns: 3
        columnSpacing: 12
        rowSpacing: 12

        function indexAt(x, y) {
          const tile = children.find(c => c.objectName.startsWith("tile:"))
          if (!tile)
            return -1
          const column = Math.max(0, Math.min(columns - 1, Math.floor((x + columnSpacing / 2) / (tile.width + columnSpacing))))
          const row = Math.max(0, Math.floor((y + rowSpacing / 2) / (tile.height + rowSpacing)))
          return Math.min(root.shownTiles.length - 1, row * columns + column)
        }

        Repeater {
          model: root.shownTiles

          QuickSettingsTile {
            id: tileItem
            required property var modelData
            required property int index
            objectName: "tile:" + modelData.key
            glyph: modelData.glyph
            label: modelData.label
            description: modelData.description
            on: modelData.on
            toggle: modelData.toggle
            hasPage: modelData.page !== ""
            editing: root.editing
            opacity: grid.dragIndex === index ? 0.5 : 1
            dropSide: grid.dragIndex < 0 || grid.dropIndex !== index || grid.dragIndex === index ? 0 : grid.dropIndex < grid.dragIndex ? -1 : 1
            onClicked: root.activate(modelData)
            title: modelData.title
            onUnpinClicked: fromKeyboard => root.unpin(modelData.key, fromKeyboard)
            onMoveRequested: offset => root.moveBy(modelData.key, offset)
            onDragStarted: grid.dragIndex = grid.dropIndex = index
            onDragMoved: (x, y) => {
              const p = tileItem.mapToItem(grid, x, y)
              grid.dropIndex = grid.indexAt(p.x, p.y)
            }
            onDragEnded: {
              const from = grid.dragIndex
              const to = grid.dropIndex
              grid.dragIndex = grid.dropIndex = -1
              if (from >= 0 && to >= 0 && from !== to) {
                const key = root.shownTiles[from].key
                const target = root.shownTiles[to].key
                // The tiles are made again for the new order, so not while this one handles its release.
                Qt.callLater(() => root.move(key, target))
              }
            }
          }
        }
      }

      Column {
        width: parent.width
        spacing: 4

        SliderRow {
          objectName: "volume"
          enabled: SystemStatus.output !== null
          glyph: SystemStatus.volumeGlyph
          label: "Volume"
          glyphLabel: SystemStatus.muted ? "Unmute" : "Mute"
          value: SystemStatus.volume
          onMoved: value => SystemStatus.output.audio.volume = value
          onGlyphClicked: SystemStatus.output.audio.muted = !SystemStatus.muted
          pageLabel: "Select a sound output"
          onPageClicked: root.page = "output"
        }

        SliderRow {
          objectName: "brightness"
          readonly property var monitor: root.brightnessMonitor
          visible: monitor !== null
          glyph: ""
          label: "Brightness"
          value: monitor ? (monitor.brightness - monitor.minimum) / Math.max(1, monitor.maximum - monitor.minimum) : 0
          onMoved: value => monitor.brightness = Math.round(monitor.minimum + value * (monitor.maximum - monitor.minimum))
        }
      }
    }

    Column {
      objectName: "powerModePage"
      visible: root.page === "powerMode"
      x: 12
      width: parent.width - 24
      topPadding: 12
      spacing: 4

      PageHeader { title: "Power mode" }

      Repeater {
        model: powerModeTile.names

        RadioButton {
          required property int index
          required property string modelData
          objectName: "powerMode:" + index
          x: 12
          text: modelData
          checked: PowerMode.mode === index
          onClicked: PowerMode.mode = index
        }
      }
    }
  }

  Column {
    objectName: "outputPage"
    visible: root.page === "output"
    x: 12
    width: parent.width - 24
    height: body.height
    topPadding: 12
    spacing: 4

    PageHeader { id: outputHeader; title: "Sound output" }

    Flickable {
      width: parent.width
      height: parent.height - parent.topPadding - outputHeader.height - moreVolume.height - 2 * parent.spacing - 12
      contentHeight: outputList.height
      clip: true
      ScrollBar.vertical: ScrollBar {}

      Column {
        id: outputList
        width: parent.width

        Repeater {
          model: root.outputs

          RadioButton {
            required property var modelData
            required property int index
            objectName: "output:" + index
            x: 12
            width: parent.width - 24
            text: modelData.description
            checked: modelData === Audio.defaultPlaybackDevice
            onClicked: Audio.defaultPlaybackDevice = modelData
          }
        }
      }
    }

    Button {
      id: moreVolume
      objectName: "moreVolume"
      x: 4
      flat: true
      text: "More volume settings"
      onClicked: root.openSettings("ms-settings:apps-volume")
    }
  }

  Column {
    objectName: "addPage"
    visible: root.page === "add"
    x: 12
    width: parent.width - 24
    topPadding: 12
    spacing: 4

    PageHeader { title: "Add" }

    Repeater {
      model: root.unpinnedTiles

      Button {
        required property var modelData
        objectName: "add:" + modelData.key
        x: 4
        width: parent.width - 8
        flat: true
        text: modelData.title
        onClicked: {
          root.setPinned(root.pinned.concat([modelData.key]))
          root.page = ""
        }

        contentItem: Row {
          spacing: 12

          Text {
            anchors.verticalCenter: parent.verticalCenter
            text: modelData.glyph
            font.family: Theme.iconFontFamily
            font.pixelSize: 16
            color: Theme.textFillColorPrimary
          }

          Text {
            anchors.verticalCenter: parent.verticalCenter
            text: modelData.title
            font: Theme.typography.body
            color: Theme.textFillColorPrimary
          }
        }
      }
    }
  }

  Rectangle {
    id: footer
    objectName: "footer"
    anchors.bottom: parent.bottom
    width: parent.width
    height: 48
    color: Theme.layerOnAcrylicFillColorDefault

    Rectangle {
      width: parent.width
      height: 1
      color: Theme.dividerStrokeColorDefault
    }

    Button {
      objectName: "battery"
      visible: SystemStatus.hasBattery && !root.editing
      anchors.left: parent.left
      anchors.leftMargin: 12
      anchors.verticalCenter: parent.verticalCenter
      flat: true
      text: "Battery " + SystemStatus.batteryPercent + "%" + (SystemStatus.charging ? ", charging" : "")
      onClicked: root.openSettings("ms-settings:batterysaver")

      contentItem: Row {
        spacing: 8

        Text {
          anchors.verticalCenter: parent.verticalCenter
          text: SystemStatus.batteryGlyph
          font.family: Theme.iconFontFamily
          font.pixelSize: 16
          color: Theme.textFillColorPrimary
        }

        Text {
          anchors.verticalCenter: parent.verticalCenter
          text: SystemStatus.batteryPercent + "%"
          font: Theme.typography.caption
          color: Theme.textFillColorPrimary
        }
      }
    }

    Row {
      visible: !root.editing
      anchors.right: parent.right
      anchors.rightMargin: 12
      anchors.verticalCenter: parent.verticalCenter

      GlyphButton {
        objectName: "edit"
        glyph: ""
        label: "Edit quick settings"
        onClicked: {
          root.page = ""
          root.editing = true
        }
      }

      GlyphButton {
        objectName: "settings"
        glyph: ""
        label: "All settings"
        onClicked: root.openSettings("ms-settings:")
      }
    }

    Row {
      visible: root.editing
      anchors.right: parent.right
      anchors.rightMargin: 12
      anchors.verticalCenter: parent.verticalCenter
      spacing: 8

      Button {
        objectName: "add"
        enabled: root.unpinnedTiles.length > 0 && root.page !== "add"
        text: "Add"
        onClicked: root.page = "add"

        contentItem: Row {
          spacing: 8

          Text {
            height: 20
            verticalAlignment: Text.AlignVCenter
            text: ""
            font.family: Theme.iconFontFamily
            font.pixelSize: 12
            color: parent.parent.enabled ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
          }

          Text {
            height: 20
            verticalAlignment: Text.AlignVCenter
            text: "Add"
            font: Theme.typography.body
            color: parent.parent.enabled ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
          }
        }
      }

      Button {
        objectName: "done"
        highlighted: true
        text: "Done"
        onClicked: {
          root.page = ""
          root.editing = false
        }
      }
    }
  }

  // A borderless button with a glyph, named by its tool tip, as WinUI's icon buttons are.
  component GlyphButton: Button {
    id: glyphButton
    property string glyph
    property string label
    width: 40
    height: 40
    padding: 0
    flat: true
    ToolTip.text: label
    ToolTip.visible: hovered
    ToolTip.delay: 1000

    contentItem: Text {
      text: glyphButton.glyph
      horizontalAlignment: Text.AlignHCenter
      verticalAlignment: Text.AlignVCenter
      font.family: Theme.iconFontFamily
      font.pixelSize: 16
      color: glyphButton.enabled ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
    }
  }

  // A glyph, which may be a button, a slider, and a chevron to a page, as in the volume and brightness rows.
  component SliderRow: Item {
    id: row
    property string glyph
    property string label
    property string glyphLabel
    property string pageLabel
    property real value
    signal moved(real value)
    signal glyphClicked()
    signal pageClicked()
    width: parent.width
    height: 40

    GlyphButton {
      objectName: "glyph"
      visible: row.glyphLabel !== ""
      anchors.verticalCenter: parent.verticalCenter
      glyph: row.glyph
      label: row.glyphLabel
      onClicked: row.glyphClicked()
    }

    Text {
      visible: row.glyphLabel === ""
      width: 40
      anchors.verticalCenter: parent.verticalCenter
      horizontalAlignment: Text.AlignHCenter
      text: row.glyph
      font.family: Theme.iconFontFamily
      font.pixelSize: 16
      color: Theme.textFillColorPrimary
      Accessible.ignored: true
    }

    Slider {
      objectName: "slider"
      x: 44
      width: parent.width - 44 - (row.pageLabel !== "" ? 44 : 0)
      anchors.verticalCenter: parent.verticalCenter
      from: 0
      to: 1
      stepSize: 0.01
      value: row.value
      Accessible.name: row.label
      onMoved: row.moved(value)
    }

    GlyphButton {
      objectName: "page"
      visible: row.pageLabel !== ""
      anchors.right: parent.right
      anchors.verticalCenter: parent.verticalCenter
      glyph: ""
      label: row.pageLabel
      onClicked: row.pageClicked()
    }
  }

  // The media session on a card, as Windows 11 shows it above quick settings: the app, the track, its art, and the
  // previous, play or pause, and next buttons.
  component MediaCard: Rectangle {
    id: card
    property var session
    width: parent.width
    height: 132
    radius: 8
    color: Theme.cardBackgroundFillColorDefault
    border.width: 1
    border.color: Theme.cardStrokeColorDefault

    Column {
      x: 16
      y: 12
      width: parent.width - 32 - (art.visible ? art.width + 12 : 0)
      spacing: 2

      Text {
        width: parent.width
        elide: Text.ElideRight
        text: card.session ? card.session.appName : ""
        font: Theme.typography.caption
        color: Theme.textFillColorSecondary
      }

      Text {
        objectName: "mediaTitle"
        width: parent.width
        topPadding: 4
        elide: Text.ElideRight
        text: card.session ? card.session.trackTitle : ""
        font: Theme.typography.bodyStrong
        color: Theme.textFillColorPrimary
      }

      Text {
        objectName: "mediaArtist"
        width: parent.width
        elide: Text.ElideRight
        text: card.session ? card.session.trackArtist : ""
        font: Theme.typography.caption
        color: Theme.textFillColorSecondary
      }
    }

    Image {
      id: art
      objectName: "mediaArt"
      visible: source !== ""
      x: parent.width - width - 16
      y: 16
      width: 56
      height: 56
      sourceSize: Qt.size(56, 56)
      fillMode: Image.PreserveAspectCrop
      source: card.session ? card.session.trackArtUrl : ""
      Accessible.ignored: true
    }

    Row {
      anchors.horizontalCenter: parent.horizontalCenter
      anchors.bottom: parent.bottom
      anchors.bottomMargin: 8
      spacing: 8

      GlyphButton {
        objectName: "previous"
        enabled: card.session ? card.session.canGoPrevious : false
        glyph: ""
        label: "Previous"
        onClicked: card.session.previous()
      }

      GlyphButton {
        objectName: "playPause"
        readonly property bool playing: card.session ? card.session.isPlaying : false
        enabled: card.session ? card.session.canTogglePlaying : false
        glyph: playing ? "" : ""
        label: playing ? "Pause" : "Play"
        onClicked: card.session.togglePlaying()
      }

      GlyphButton {
        objectName: "next"
        enabled: card.session ? card.session.canGoNext : false
        glyph: ""
        label: "Next"
        onClicked: card.session.next()
      }
    }
  }

  // The back button and title at the top of a page.
  component PageHeader: Row {
    property string title
    spacing: 4

    GlyphButton {
      objectName: "back"
      glyph: ""
      label: "Back"
      onClicked: root.page = ""
    }

    Text {
      height: 40
      verticalAlignment: Text.AlignVCenter
      text: parent.title
      font: Theme.typography.bodyStrong
      color: Theme.textFillColorPrimary
    }
  }
}
