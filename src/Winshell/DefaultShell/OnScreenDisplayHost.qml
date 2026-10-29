import QtQuick
import Winshell
import Winshell.Audio
import Winshell.Power
import Winshell.Media
import Winshell.Theme

// Binds the on-screen display to the audio, brightness and media services. It only reads levels and mute.
// Transport buttons are the one write, and they go to the media session the user is already playing.
Scope {
  id: host

  function screenIdFor(monitor) {
    var screens = Shell.screens
    for (var i = 0; i < screens.length; i++) {
      var s = screens[i]
      if (monitor.deviceName === s.name || monitor.deviceName === ("\\\\.\\" + s.name) || (monitor.name && monitor.name === s.model))
        return s.name
    }
    return ""
  }

  property var playback: Audio.defaultPlaybackDevice
  property var recording: Audio.defaultRecordingDevice
  // The session Windows would show: one that is playing, or else one that is paused.
  property var session: {
    var list = Media.sessions.values
    var paused = null
    for (var i = 0; i < list.length; i++) {
      var s = list[i]
      if (s.playbackState === MediaPlaybackState.Playing)
        return s
      if (paused === null && s.playbackState === MediaPlaybackState.Paused)
        paused = s
    }
    return paused
  }

  OnScreenDisplay {
    id: osd
    animationsEnabled: Theme.animationsEnabled
    contrast: Theme.highContrast
    volume: host.playback ? host.playback.audio.volume : 0
    playbackMuted: host.playback ? host.playback.audio.muted : false
    volumeKnown: host.playback !== null
    microphoneMuted: host.recording ? host.recording.audio.muted : false
    microphoneKnown: host.recording !== null
    mediaTitle: host.session ? host.session.trackTitle : ""
    mediaArtist: host.session ? host.session.trackArtist : ""
    mediaArtUrl: host.session ? host.session.trackArtUrl : ""
    mediaPlaying: host.session ? host.session.isPlaying : false
    mediaKnown: host.session !== null
    canTogglePlaying: host.session ? host.session.canTogglePlaying : false
    canGoNext: host.session ? host.session.canGoNext : false
    canGoPrevious: host.session ? host.session.canGoPrevious : false
    onTogglePlayback: {
      if (host.session && host.session.canTogglePlaying)
        host.session.togglePlaying()
    }
    onNextTrack: {
      if (host.session && host.session.canGoNext)
        host.session.next()
    }
    onPreviousTrack: {
      if (host.session && host.session.canGoPrevious)
        host.session.previous()
    }

    Repeater {
      model: Brightness.monitors
      Item {
        required property var modelData
        property int level: modelData.brightness
        visible: false
        width: 0
        height: 0

        function publish() {
          osd.brightnessScreenId = host.screenIdFor(modelData)
          osd.brightnessMinimum = modelData.minimum
          osd.brightnessMaximum = modelData.maximum
          // The level is written before the source is known, so the first reading is the baseline.
          osd.brightness = level
          osd.brightnessKnown = true
        }

        Component.onCompleted: publish()
        onLevelChanged: publish()
      }
    }
  }
}
