# Winshell.Media

What is playing, and play, pause, skip and seek. Per-app volume stays on the audio session. Reach media from `Shell.Current.Media`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## MediaLoopState

What a MediaSession repeats.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 | Nothing repeats. |
| `Track` | 1 | The track repeats. |
| `List` | 2 | The whole list repeats. |

## MediaPlaybackState

Whether a MediaSession is playing.

| Value | Number | Description |
| --- | --- | --- |
| `Stopped` | 0 | Nothing is playing or paused, or Windows reports the session closed or only opened. |
| `Playing` | 1 | The app is playing. |
| `Paused` | 2 | The app is paused. |

## MediaService

The media sessions Windows shows in the media controls, one for each app that plays media.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Sessions` | ObservableCollection of [MediaSession](#mediasession), read-only | One session for each app in the Windows media controls. Empty when no app plays media. |
| `Dispose()` | void | Stops following the Windows media sessions. The ones in Sessions stop updating. |

## MediaSession

One app's entry in the Windows media controls.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppId` | string, read-only | The app's AppUserModelID, such as `Spotify.exe` or a Store app's id. |
| `AppName` | string, read-only | The name Windows shows for the app. |
| `CanControl` | bool, read-only | True when the session accepts at least one playback command. |
| `CanGoNext` | bool, read-only | Whether the app accepts Next. |
| `CanGoPrevious` | bool, read-only | Whether the app accepts Previous. |
| `CanPause` | bool, read-only | Whether the app accepts Pause. |
| `CanPlay` | bool, read-only | Whether the app accepts Play. |
| `CanSeek` | bool, read-only | Whether the app accepts a new Position and Seek. |
| `CanStop` | bool, read-only | Whether the app accepts Stop. |
| `CanSwitchTo` | bool, read-only | True when SwitchTo knows how to find the app: a Store app, or a program whose AppId is its file name. |
| `CanTogglePlaying` | bool, read-only | Whether TogglePlaying can work: CanPause while playing, CanPlay otherwise. |
| `IsPlaying` | bool | True while playing. Setting it plays or pauses. |
| `Length` | double, read-only | The track's length in seconds. When the app does not report one, it is the same as Position. |
| `LengthSupported` | bool, read-only | Whether the app reports the track's length. |
| `LoopState` | [MediaLoopState](#medialoopstate) | What the app repeats. MediaLoopState.None when LoopSupported is false, and then setting it does nothing. |
| `LoopSupported` | bool, read-only | Whether the app accepts a new LoopState. |
| `MaxRate` | double, read-only | The fastest Rate to set: 4 when the app accepts rate changes, otherwise 1. |
| `Metadata` | IReadOnlyDictionary of string, object, read-only | The track as Windows reports it: `title`, `artist`, `album` and `albumArtist`, and when the app gives them, `artUrl`, `duration` in seconds, `trackNumber`, and `genres`, a list. |
| `MinRate` | double, read-only | Windows does not say which rates an app accepts; these are the common ones when it takes any. |
| `PlaybackState` | [MediaPlaybackState](#mediaplaybackstate) | Setting it calls Play, Pause or Stop. |
| `Position` | double | Where playback is in the track, in seconds. It counts forward while playing, but a change is only raised when the app reports a position, seeks, changes track, or pauses or plays. To show it moving, call RefreshPosition from a timer. |
| `PositionSupported` | bool, read-only | Whether the app reports where playback is. When false, Position is 0. |
| `Rate` | double | The playback speed; 1 is normal. |
| `Shuffle` | bool | Whether the app plays in shuffled order. False when ShuffleSupported is false, and then setting it does nothing. |
| `ShuffleSupported` | bool, read-only | Whether the app accepts a new Shuffle. |
| `TrackAlbum` | string, read-only | The track's album. Empty when the app gives none. |
| `TrackAlbumArtist` | string, read-only | The artist of the track's album. Empty when the app gives none. |
| `TrackArtUrl` | string, read-only | A `file:///` url of the art Windows gives, written to the temp folder. |
| `TrackArtist` | string, read-only | The track's artist. Empty when the app gives none. |
| `TrackTitle` | string, read-only | The track's title. Empty when the app gives none. |
| `UniqueId` | uint, read-only | A number that goes up by one each time the title, artist or album changes. |
| `PostTrackChanged` | Action | The track changed, after the track's properties are updated. |
| `TrackChanged` | Action | The track changed. It comes before the track's properties are updated. |
| `Next()` | void | Asks the app to skip to the next track. Logs a warning when CanGoNext is false. |
| `Pause()` | void | Asks the app to pause. Logs a warning when CanPause is false. |
| `Play()` | void | Asks the app to play. Logs a warning when CanPlay is false. |
| `Previous()` | void | Asks the app to go back a track. Logs a warning when CanGoPrevious is false. |
| `RefreshPosition()` | void | Raises a change of Position, so a binding reads where playback has got to. |
| `Seek(double offset)` | void | Windows has no relative seek, so this sets the position to the current one plus the offset. |
| `Stop()` | void | Asks the app to stop. Logs a warning when CanStop is false. |
| `SwitchTo()` | void | Brings the app's window forward. A program's window is looked for off the UI thread, so it comes forward a moment later, and a warning is logged when the program has none. |
| `TogglePlaying()` | void | Calls Pause while playing, and Play otherwise. |
