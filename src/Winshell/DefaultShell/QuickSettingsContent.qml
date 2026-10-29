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
// the user moves a slider or picks a power mode or an output.
Item {
  id: root

  // The page shown instead of the tiles, "powerMode" or "output", or "" for the tiles.
  property string page: ""
  readonly property var mediaSession: Media.sessions.values.find(s => s.isPlaying) ?? Media.sessions.values[0] ?? null
  readonly property var outputs: Audio.nodes.values.filter(n => n.isPlayback && !n.isSession)
  readonly property var tiles: [wifiTile, bluetoothTile, airplaneTile, energySaverTile, powerModeTile, nightLightTile, accessibilityTile]
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

  implicitWidth: 360
  implicitHeight: body.implicitHeight + footer.height
  focus: true
  Keys.onEscapePressed: page !== "" ? page = "" : closeRequested()

  component TileSpec: QtObject {
    property string key
    property string glyph
    property string label
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
    glyph: ""
    label: SystemStatus.wifiNetwork ? SystemStatus.wifiNetwork.name : "Wi-Fi"
    description: "Opens Wi-Fi settings"
    on: Networking.wifiEnabled
    available: SystemStatus.wifi !== null || Networking.wifiState !== WifiRadioState.Unknown
    settingsUri: "ms-settings:network-wifi"
  }

  TileSpec {
    id: bluetoothTile
    readonly property var connected: Bluetooth.devices.values.find(d => d.connected) ?? null
    key: "bluetooth"
    glyph: ""
    label: connected ? connected.name : "Bluetooth"
    description: "Opens Bluetooth settings"
    on: Bluetooth.defaultAdapter ? Bluetooth.defaultAdapter.enabled : false
    available: Bluetooth.defaultAdapter !== null
    settingsUri: "ms-settings:bluetooth"
  }

  TileSpec {
    id: airplaneTile
    key: "airplaneMode"
    glyph: ""
    label: "Airplane mode"
    description: "Opens airplane mode settings"
    on: Networking.airplaneMode === AirplaneModeState.On
    settingsUri: "ms-settings:network-airplanemode"
  }

  TileSpec {
    id: energySaverTile
    key: "energySaver"
    glyph: ""
    label: "Energy saver"
    description: "Opens energy saver settings"
    on: SystemStatus.energySaver
    settingsUri: "ms-settings:batterysaver"
  }

  TileSpec {
    id: powerModeTile
    readonly property var names: ["Best power efficiency", "Balanced", "Best performance"]
    key: "powerMode"
    glyph: ""
    label: "Power mode"
    description: names[PowerMode.mode] ?? ""
    toggle: false
    available: PowerMode.available
    page: "powerMode"
  }

  TileSpec {
    id: nightLightTile
    key: "nightLight"
    glyph: ""
    label: "Night light"
    description: "Opens night light settings"
    // Windows has no API that reads night light, so the tile does not pretend to know.
    toggle: false
    settingsUri: "ms-settings:nightlight"
  }

  TileSpec {
    id: accessibilityTile
    key: "accessibility"
    glyph: ""
    label: "Accessibility"
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
        columns: 3
        columnSpacing: 12
        rowSpacing: 12

        Repeater {
          model: root.tiles.filter(t => t.available)

          QuickSettingsTile {
            required property var modelData
            objectName: "tile:" + modelData.key
            glyph: modelData.glyph
            label: modelData.label
            description: modelData.description
            on: modelData.on
            toggle: modelData.toggle
            hasPage: modelData.page !== ""
            onClicked: root.activate(modelData)
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
      visible: SystemStatus.hasBattery
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
          font.pixelSize: 12
          color: Theme.textFillColorPrimary
        }
      }
    }

    GlyphButton {
      objectName: "settings"
      anchors.right: parent.right
      anchors.rightMargin: 12
      anchors.verticalCenter: parent.verticalCenter
      glyph: ""
      label: "All settings"
      onClicked: root.openSettings("ms-settings:")
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
        font.pixelSize: 12
        color: Theme.textFillColorSecondary
      }

      Text {
        objectName: "mediaTitle"
        width: parent.width
        topPadding: 4
        elide: Text.ElideRight
        text: card.session ? card.session.trackTitle : ""
        font.pixelSize: 14
        font.weight: Font.DemiBold
        color: Theme.textFillColorPrimary
      }

      Text {
        objectName: "mediaArtist"
        width: parent.width
        elide: Text.ElideRight
        text: card.session ? card.session.trackArtist : ""
        font.pixelSize: 12
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
      font.pixelSize: 14
      font.weight: Font.DemiBold
      color: Theme.textFillColorPrimary
    }
  }
}
