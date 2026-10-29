# Winshell.Media

`import Winshell.Media`. The media sessions Windows shows in its media controls: what each app is playing, and play, pause, skip and seek for it. A shell uses it for a media card in quick settings or play/pause buttons on the on-screen display. A call or a write goes to the app through Windows, and the app decides whether to follow it.

Per-app volume is not here: it stays on the app's audio session in [Winshell.Audio](winshell-audio.md), because Windows media controls have no volume of their own. [Migrating from Quickshell](../migrating-from-quickshell.md#media) maps the Mpris names onto these.

## Media

The `Media` singleton: one [MediaSession](#mediasession) for each session the Windows media controls know. Reach it as `Media`, for example `Media.sessions`.

| Member | Type | Description |
| --- | --- | --- |
| `sessions` | ObjectModel of [MediaSession](#mediasession), read-only | The media sessions Windows shows in the media controls. A session is added when an app starts one and removed when it ends. |

## MediaSession

One app's entry in the Windows media controls. Sessions are in `Media.sessions`; QML cannot create one.

A control that the app does not allow at the moment, such as `next()` while `canGoNext` is false, does nothing and writes a warning to the log.

| Member | Type | Description |
| --- | --- | --- |
| `canPlay` | bool, read-only | Whether the app accepts play now. |
| `canPause` | bool, read-only | Whether the app accepts pause now. |
| `canStop` | bool, read-only | Whether the app accepts stop now. |
| `canTogglePlaying` | bool, read-only | Whether `togglePlaying()` can act: `canPause` while playing, `canPlay` otherwise. |
| `canSeek` | bool, read-only | Whether the app accepts a new position. |
| `canGoNext` | bool, read-only | Whether the app accepts skipping to the next track. |
| `canGoPrevious` | bool, read-only | Whether the app accepts going to the previous track. |
| `canSwitchTo` | bool, read-only | True when Windows can bring the app's window forward: the app is a Store app, or its program has a main window. It is worked out each time it is read, and no change is reported. |
| `appName` | string, read-only | The name Windows shows for the app. |
| `appId` | string, read-only | The app's AppUserModelID, such as `Spotify.exe` or a Store app's id. |
| `position` | real | Where playback is in the track, in seconds. It counts forward while playing. Setting it seeks the app when `positionSupported` and `canSeek` are true. |
| `positionSupported` | bool, read-only | Whether the app reports a position. When it is false, `position` is 0. |
| `length` | real, read-only | The track's length in seconds. When the app does not report one, it is the same as `position`. |
| `lengthSupported` | bool, read-only | Whether the app reports a length. |
| `metadata` | var, read-only | The track as Windows reports it: `title`, `artist`, `album` and `albumArtist`, and when the app gives them, `artUrl`, `duration` in seconds, `trackNumber`, and `genres`, a list. |
| `uniqueId` | int, read-only | A number that goes up by one each time the title, artist or album changes. |
| `trackTitle` | string, read-only | The track's title. Empty when the app gives none. |
| `trackArtist` | string, read-only | The track's artist, as one string. |
| `trackAlbum` | string, read-only | The track's album. |
| `trackAlbumArtist` | string, read-only | The album's artist. |
| `trackArtUrl` | string, read-only | A `file:///` url of the art Windows gives, written to the temp folder. Empty when there is no art. Art over 8 MiB or 4096 by 4096 pixels, or not a PNG, JPEG, GIF, BMP or WebP picture, is ignored. |
| `playbackState` | [MediaPlaybackState](#mediaplaybackstate) | Whether the app is playing, paused or stopped. Setting it calls `play()`, `pause()` or `stop()`. |
| `isPlaying` | bool | True while playing. Setting it plays or pauses. |
| `loopState` | [MediaLoopState](#medialoopstate) | Whether the app repeats the track or the list. Setting it changes the app's repeat when `loopSupported` is true. |
| `loopSupported` | bool, read-only | Whether the app lets Windows change its repeat. |
| `rate` | real | The playback speed; 1 is normal. Setting it asks the app for that speed, from `minRate` to `maxRate`. |
| `minRate` | real, read-only | 0.25 when the app takes a speed, otherwise 1. Windows does not say which speeds an app accepts; these are the common ones. |
| `maxRate` | real, read-only | 4 when the app takes a speed, otherwise 1. |
| `shuffle` | bool | Whether the app shuffles. Setting it changes the app's shuffle when `shuffleSupported` is true. |
| `shuffleSupported` | bool, read-only | Whether the app lets Windows change its shuffle. |
| `play()` | method | Starts playing when `canPlay` is true. |
| `pause()` | method | Pauses when `canPause` is true. |
| `stop()` | method | Stops when `canStop` is true. |
| `togglePlaying()` | method | Pauses while playing, and plays otherwise. |
| `next()` | method | Skips to the next track when `canGoNext` is true. |
| `previous()` | method | Goes to the previous track when `canGoPrevious` is true. |
| `seek(offset)` | method | Moves the position by `offset` seconds, forward or back, when `canSeek` is true. Windows has no relative seek, so this sets the position to the current one plus the offset. |
| `switchTo()` | method | Brings the app's window forward, restoring it when it is minimized. A Store app with no window found is started again, which brings its window forward. |
| `trackChanged()` | signal | The track changed. It comes before the track's properties are updated. |
| `postTrackChanged()` | signal | The track changed, after the track's properties are updated. |

A binding on `position` updates when the app reports a position, seeks, changes track, or pauses or plays. To show it moving while playing, call `positionChanged()` from a `Timer`, as the progress bar below does.

## MediaPlaybackState

The values of `MediaSession.playbackState`.

| Value | Meaning |
| --- | --- |
| `Stopped` | Nothing is playing, or the app reports something other than playing or paused. |
| `Playing` | The app is playing. |
| `Paused` | The app is paused. |

## MediaLoopState

The values of `MediaSession.loopState`.

| Value | Meaning |
| --- | --- |
| `None` | No repeat. |
| `Track` | The track repeats. |
| `List` | The list repeats. |

## Examples

Play/pause and skip for the first media session.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Media

Column {
    readonly property var session: Media.sessions.values[0] ?? null

    Label {
        text: session ? session.trackTitle + " - " + session.trackArtist : "Nothing playing"
    }
    Row {
        Button {
            text: "Previous"
            enabled: session !== null && session.canGoPrevious
            onClicked: session.previous()
        }
        Button {
            text: session !== null && session.isPlaying ? "Pause" : "Play"
            enabled: session !== null && session.canTogglePlaying
            onClicked: session.togglePlaying()
        }
        Button {
            text: "Next"
            enabled: session !== null && session.canGoNext
            onClicked: session.next()
        }
    }
}
```

A progress bar for the first session, moved along once a second while it plays.

```qml
import QtQuick
import Winshell.Media

Rectangle {
    id: bar
    readonly property var session: Media.sessions.values[0] ?? null
    width: 300
    height: 4
    color: "#33000000"

    Timer {
        interval: 1000
        repeat: true
        running: bar.session !== null && bar.session.isPlaying
        onTriggered: bar.session.positionChanged()
    }
    Rectangle {
        height: parent.height
        width: bar.session && bar.session.lengthSupported ? parent.width * bar.session.position / bar.session.length : 0
        color: "#0078d4"
    }
}
```
