import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls

// The Windows 11 on-screen display: a short panel centered on the bottom of the work area, 8 logical pixels
// above the taskbar. Volume, brightness and microphone mute stay for 3 seconds; a media change stays for 5.
// The pointer holding it keeps it open. It never writes volume, brightness or mute. Media buttons call the session.
// With animation effects off the fade is skipped. A contrast theme uses the system window color, a 2px border and
// no Acrylic. The first reading after startup is the baseline and does not show it.
PanelWindow {
  id: root

  objectName: "osd"

  readonly property int panelWidth: 360
  readonly property int bottomGap: 8
  readonly property int levelHideMs: 3000
  readonly property int mediaHideMs: 5000
  readonly property int settleMs: 400

  property bool animationsEnabled: true
  property bool contrast: false

  property real volume: 0
  property bool playbackMuted: false
  property bool volumeKnown: false
  property int brightness: 0
  property int brightnessMinimum: 0
  property int brightnessMaximum: 100
  property bool brightnessKnown: false
  property string brightnessScreenId: ""
  property var brightnessScreen: null
  property bool microphoneMuted: false
  property bool microphoneKnown: false
  property string mediaTitle: ""
  property string mediaArtist: ""
  property string mediaArtUrl: ""
  property bool mediaPlaying: false
  property bool mediaKnown: false
  property bool canTogglePlaying: false
  property bool canGoNext: false
  property bool canGoPrevious: false

  readonly property bool showMedia: (mode === "media" || mode === "volume") && mediaKnown
  readonly property bool showLevel: mode === "brightness" || mode === "volume" || (mode === "media" && volumeKnown)
  readonly property bool showMicrophone: mode === "microphone"

  property string mode: ""
  property bool open: false
  property bool hiding: false
  property bool settled: false
  property bool pointerInside: false
  property string announcement: ""

  property bool sawVolume: false
  property real seenVolume: 0
  property bool seenMuted: false
  property bool sawBrightness: false
  property int seenBrightness: 0
  property bool sawMicrophone: false
  property bool seenMicrophone: false
  property bool sawMedia: false
  property string seenTitle: ""
  property string seenArtist: ""
  property bool seenPlaying: false

  signal togglePlayback()
  signal nextTrack()
  signal previousTrack()

  visible: open
  implicitWidth: panelWidth
  implicitHeight: showMedia && showLevel ? 120 : showMedia ? 80 : 64
  color: "transparent"
  anchors.bottom: true
  exclusiveZone: 0
  aboveWindows: true
  focusable: false
  margins.bottom: bottomGap
  screen: mode === "brightness" && brightnessScreen !== null ? brightnessScreen : (Shell.screens.length > 0 ? Shell.screens[0] : null)
  WindowEffects.backdrop: contrast ? WindowEffects.None : Preferences.material
  WindowEffects.corners: WindowEffects.RoundCorners

  onVolumeChanged: noteVolume()
  onPlaybackMutedChanged: noteVolume()
  onVolumeKnownChanged: noteVolume()
  onBrightnessChanged: noteBrightness()
  onBrightnessKnownChanged: noteBrightness()
  onMicrophoneMutedChanged: noteMicrophone()
  onMicrophoneKnownChanged: noteMicrophone()
  onMediaTitleChanged: noteMedia()
  onMediaArtistChanged: noteMedia()
  onMediaPlayingChanged: noteMedia()
  onMediaKnownChanged: noteMedia()
  onPointerInsideChanged: {
    if (!open)
      return
    if (pointerInside) {
      hideTimer.stop()
      if (hiding) {
        hiding = false
        fade.stop()
        card.opacity = 1
      }
    } else {
      armTimer()
    }
  }

  Timer {
    id: settle
    interval: root.settleMs
    running: true
    onTriggered: root.settleNow()
  }

  Timer {
    id: hideTimer
    onTriggered: root.dismiss()
  }

  NumberAnimation {
    id: fade
    target: card
    property: "opacity"
    duration: 0
    easing.bezierCurve: Theme.controlFastOutSlowInKeySpline
    onFinished: if (root.hiding) root.open = false
  }

  function settleNow() {
    if (volumeKnown) {
      sawVolume = true
      seenVolume = volume
      seenMuted = playbackMuted
    }
    if (brightnessKnown) {
      sawBrightness = true
      seenBrightness = brightness
      brightnessScreen = resolveScreen(brightnessScreenId)
    }
    if (microphoneKnown) {
      sawMicrophone = true
      seenMicrophone = microphoneMuted
    }
    if (mediaKnown) {
      sawMedia = true
      captureMedia()
    }
    settled = true
  }

  function noteVolume() {
    if (!settled || !volumeKnown)
      return
    if (!sawVolume) {
      sawVolume = true
      seenVolume = volume
      seenMuted = playbackMuted
      return
    }
    if (seenVolume === volume && seenMuted === playbackMuted)
      return
    seenVolume = volume
    seenMuted = playbackMuted
    show("volume")
  }

  function noteBrightness() {
    if (publishing || !settled || !brightnessKnown)
      return
    if (!sawBrightness) {
      sawBrightness = true
      seenBrightness = brightness
      brightnessScreen = resolveScreen(brightnessScreenId)
      return
    }
    if (seenBrightness === brightness)
      return
    seenBrightness = brightness
    brightnessScreen = resolveScreen(brightnessScreenId)
    show("brightness")
  }

  // One monitor's reading, from a host that watches several. A change shows even when it lands on the level another
  // monitor published last; a reading that is not a change, such as a monitor that appears, is only its baseline.
  property bool publishing: false
  function publishBrightness(screenId, minimum, maximum, level, changed) {
    publishing = true
    brightnessScreenId = screenId
    brightnessMinimum = minimum
    brightnessMaximum = maximum
    brightness = level
    brightnessKnown = true
    publishing = false
    if (!settled)
      return
    sawBrightness = true
    seenBrightness = level
    if (!changed)
      return
    brightnessScreen = resolveScreen(screenId)
    show("brightness")
  }

  function noteMicrophone() {
    if (!settled || !microphoneKnown)
      return
    if (!sawMicrophone) {
      sawMicrophone = true
      seenMicrophone = microphoneMuted
      return
    }
    if (seenMicrophone === microphoneMuted)
      return
    seenMicrophone = microphoneMuted
    show("microphone")
  }

  function noteMedia() {
    if (!settled || !mediaKnown)
      return
    if (!sawMedia) {
      sawMedia = true
      captureMedia()
      return
    }
    if (seenTitle === mediaTitle && seenArtist === mediaArtist && seenPlaying === mediaPlaying)
      return
    captureMedia()
    show("media")
  }

  function captureMedia() {
    seenTitle = mediaTitle
    seenArtist = mediaArtist
    seenPlaying = mediaPlaying
  }

  function resolveScreen(id) {
    var screens = Shell.screens
    var fallback = screens.length > 0 ? screens[0] : null
    if (!id)
      return fallback
    for (var i = 0; i < screens.length; i++) {
      var s = screens[i]
      if (id === s.name || id === ("\\\\.\\" + s.name) || id === s.model)
        return s
    }
    return fallback
  }

  function percent(value, from, to) {
    if (to === from)
      return 0
    var p = Math.round((value - from) / (to - from) * 100)
    if (p < 0)
      return 0
    if (p > 100)
      return 100
    return p
  }

  function mediaLine() {
    var state = mediaPlaying ? "Playing" : "Paused"
    var who = mediaTitle
    if (mediaArtist.length > 0)
      who = who.length > 0 ? who + ", " + mediaArtist : mediaArtist
    return who.length > 0 ? state + " " + who : state
  }

  function updateAnnouncement() {
    if (mode === "volume") {
      var text = playbackMuted ? "Volume muted" : "Volume " + percent(volume, 0, 1) + " percent"
      if (mediaKnown)
        text += ". " + mediaLine()
      announcement = text
    } else if (mode === "brightness") {
      announcement = "Brightness " + percent(brightness, brightnessMinimum, brightnessMaximum) + " percent"
    } else if (mode === "microphone") {
      announcement = microphoneMuted ? "Microphone muted" : "Microphone on"
    } else if (mode === "media") {
      announcement = mediaLine()
    }
  }

  function show(kind) {
    hiding = false
    mode = kind
    updateAnnouncement()
    open = true
    fade.duration = animationsEnabled ? Theme.controlFastAnimationDuration : 0
    fade.from = card.opacity
    fade.to = 1
    fade.restart()
    if (pointerInside)
      hideTimer.stop()
    else
      armTimer()
  }

  function dismiss() {
    if (!open || pointerInside)
      return
    hiding = true
    hideTimer.stop()
    fade.duration = animationsEnabled ? Theme.controlFastAnimationDuration : 0
    fade.from = card.opacity
    fade.to = 0
    fade.restart()
  }

  function armTimer() {
    hideTimer.interval = mode === "media" ? mediaHideMs : levelHideMs
    hideTimer.restart()
  }

  function syncPointer() {
    pointerInside = cardHover.containsMouse
      || (showLevel && levelShield.containsMouse)
      || (showMedia && previousButton.containsMouse)
      || (showMedia && playButton.containsMouse)
      || (showMedia && nextButton.containsMouse)
  }

  Rectangle {
    id: card
    objectName: "card"
    anchors.fill: parent
    radius: Theme.overlayCornerRadius
    clip: true
    opacity: 0
    color: root.contrast ? Theme.systemColorWindowColor : "transparent"
    border.width: root.contrast ? 2 : 1
    border.color: root.contrast ? Theme.systemColorWindowTextColor : Theme.surfaceStrokeColorFlyout
    Accessible.role: Accessible.Notification
    Accessible.name: root.announcement

    MouseArea {
      id: cardHover
      anchors.fill: parent
      hoverEnabled: true
      acceptedButtons: Qt.NoButton
      onContainsMouseChanged: root.syncPointer()
    }

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: 16
      spacing: 8

      RowLayout {
        visible: root.showMedia
        Layout.fillWidth: true
        Layout.preferredHeight: 48
        spacing: 12

        Rectangle {
          Layout.preferredWidth: 48
          Layout.preferredHeight: 48
          radius: 4
          clip: true
          color: Theme.subtleFillColorSecondary

          Image {
            anchors.fill: parent
            source: root.mediaArtUrl
            fillMode: Image.PreserveAspectCrop
            visible: root.mediaArtUrl.length > 0
            Accessible.ignored: true
          }

          Text {
            anchors.centerIn: parent
            visible: root.mediaArtUrl.length === 0
            text: "\uE8D6"
            font.family: Theme.iconFontFamily
            font.pixelSize: 20
            color: Theme.textFillColorSecondary
            Accessible.ignored: true
          }
        }

        ColumnLayout {
          Layout.fillWidth: true
          spacing: 0

          Text {
            Layout.fillWidth: true
            text: root.mediaTitle.length > 0 ? root.mediaTitle : "Media"
            elide: Text.ElideRight
            font: Theme.typography.bodyStrong
            color: Theme.textFillColorPrimary
          }

          Text {
            Layout.fillWidth: true
            visible: root.mediaArtist.length > 0
            text: root.mediaArtist
            elide: Text.ElideRight
            font: Theme.typography.caption
            color: Theme.textFillColorSecondary
          }
        }

        TransportButton {
          id: previousButton
          objectName: "previous"
          enabled: root.canGoPrevious
          accessibleName: "Previous"
          glyph: "\uE892"
          onActivated: root.previousTrack()
        }

        TransportButton {
          id: playButton
          objectName: "play"
          enabled: root.canTogglePlaying
          accessibleName: root.mediaPlaying ? "Pause" : "Play"
          glyph: root.mediaPlaying ? "\uE769" : "\uE768"
          onActivated: root.togglePlayback()
        }

        TransportButton {
          id: nextButton
          objectName: "next"
          enabled: root.canGoNext
          accessibleName: "Next"
          glyph: "\uE893"
          onActivated: root.nextTrack()
        }
      }

      RowLayout {
        visible: root.showLevel
        Layout.fillWidth: true
        Layout.preferredHeight: 32
        spacing: 16

        Text {
          Layout.alignment: Qt.AlignVCenter
          text: root.mode === "brightness" ? "\uE706" : (root.playbackMuted || root.volume <= 0 ? "\uE74F" : root.volume < 0.33 ? "\uE993" : root.volume < 0.66 ? "\uE994" : "\uE995")
          font.family: Theme.iconFontFamily
          font.pixelSize: 16
          color: Theme.textFillColorPrimary
          Accessible.ignored: true
        }

        Item {
          Layout.fillWidth: true
          implicitHeight: 32

          Slider {
            id: level
            objectName: "level"
            anchors.fill: parent
            from: root.mode === "brightness" ? root.brightnessMinimum : 0
            to: root.mode === "brightness" ? root.brightnessMaximum : 1
            value: root.mode === "brightness" ? root.brightness : root.volume
            Accessible.name: root.mode === "brightness" ? "Brightness" : "Volume"
          }

          // The hardware indicator shows the level. Presses stay here so the slider cannot change the device.
          MouseArea {
            id: levelShield
            anchors.fill: parent
            hoverEnabled: true
            onContainsMouseChanged: root.syncPointer()
          }
        }
      }

      RowLayout {
        visible: root.showMicrophone
        Layout.fillWidth: true
        Layout.preferredHeight: 32
        spacing: 16

        Text {
          Layout.alignment: Qt.AlignVCenter
          text: root.microphoneMuted ? "\uF781" : "\uE720"
          font.family: Theme.iconFontFamily
          font.pixelSize: 16
          color: Theme.textFillColorPrimary
          Accessible.ignored: true
        }

        Text {
          Layout.fillWidth: true
          Layout.alignment: Qt.AlignVCenter
          text: root.microphoneMuted ? "Microphone muted" : "Microphone on"
          font: Theme.typography.body
          color: Theme.textFillColorPrimary
        }
      }
    }
  }

  component TransportButton: MouseArea {
    id: button
    property string glyph: ""
    property string accessibleName: ""
    signal activated()
    implicitWidth: 32
    implicitHeight: 32
    hoverEnabled: true
    Layout.alignment: Qt.AlignVCenter
    onClicked: if (button.enabled) button.activated()
    onContainsMouseChanged: root.syncPointer()
    Accessible.role: Accessible.Button
    Accessible.name: button.accessibleName
    Accessible.onPressAction: if (button.enabled) button.activated()

    Rectangle {
      anchors.fill: parent
      radius: 4
      color: !button.enabled ? "transparent" : button.pressed ? Theme.subtleFillColorTertiary : button.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
    }

    Text {
      anchors.centerIn: parent
      text: button.glyph
      font.family: Theme.iconFontFamily
      font.pixelSize: 16
      color: button.enabled ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
      Accessible.ignored: true
    }
  }
}
