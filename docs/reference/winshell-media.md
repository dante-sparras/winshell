# Winshell.Media

What is playing, and play, pause, skip and seek. Per-app volume stays on the audio session. Reach media from `Shell.Current.Media`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## MediaLoopState

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 |  |
| `Track` | 1 |  |
| `List` | 2 |  |

## MediaPlaybackState

| Value | Number | Description |
| --- | --- | --- |
| `Stopped` | 0 |  |
| `Playing` | 1 |  |
| `Paused` | 2 |  |

## MediaService

The media sessions Windows shows in the media controls, one for each app that plays media.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Sessions` | ObservableCollection of [MediaSession](#mediasession), read-only |  |
| `Dispose()` | void |  |

## MediaSession

One app's entry in the Windows media controls.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppId` | string, read-only | The app's AppUserModelID, such as `Spotify.exe` or a Store app's id. |
| `AppName` | string, read-only | The name Windows shows for the app. |
| `CanControl` | bool, read-only | True when the session accepts at least one playback command. |
| `CanGoNext` | bool, read-only |  |
| `CanGoPrevious` | bool, read-only |  |
| `CanPause` | bool, read-only |  |
| `CanPlay` | bool, read-only |  |
| `CanSeek` | bool, read-only |  |
| `CanStop` | bool, read-only |  |
| `CanSwitchTo` | bool, read-only | True when Windows can bring the app's window forward. It is worked out on each read. |
| `CanTogglePlaying` | bool, read-only |  |
| `IsPlaying` | bool | True while playing. Setting it plays or pauses. |
| `Length` | double, read-only | The track's length in seconds. When the app does not report one, it is the same as Position. |
| `LengthSupported` | bool, read-only |  |
| `LoopState` | [MediaLoopState](#medialoopstate) |  |
| `LoopSupported` | bool, read-only |  |
| `MaxRate` | double, read-only |  |
| `Metadata` | IReadOnlyDictionary of string, object, read-only | The track as Windows reports it: `title`, `artist`, `album` and `albumArtist`, and when the app gives them, `artUrl`, `duration` in seconds, `trackNumber`, and `genres`, a list. |
| `MinRate` | double, read-only | Windows does not say which rates an app accepts; these are the common ones when it takes any. |
| `PlaybackState` | [MediaPlaybackState](#mediaplaybackstate) | Setting it calls Play, Pause or Stop. |
| `Position` | double | Where playback is in the track, in seconds. It counts forward while playing, but a change is only raised when the app reports a position, seeks, changes track, or pauses or plays. To show it moving, call RefreshPosition from a timer. |
| `PositionSupported` | bool, read-only |  |
| `Rate` | double | The playback speed; 1 is normal. |
| `Shuffle` | bool |  |
| `ShuffleSupported` | bool, read-only |  |
| `TrackAlbum` | string, read-only |  |
| `TrackAlbumArtist` | string, read-only |  |
| `TrackArtUrl` | string, read-only | A `file:///` url of the art Windows gives, written to the temp folder. |
| `TrackArtist` | string, read-only |  |
| `TrackTitle` | string, read-only |  |
| `UniqueId` | UInt32, read-only | A number that goes up by one each time the title, artist or album changes. |
| `PostTrackChanged` | Action |  |
| `TrackChanged` | Action |  |
| `Next()` | void |  |
| `Pause()` | void |  |
| `Play()` | void |  |
| `Previous()` | void |  |
| `RefreshPosition()` | void | Raises a change of Position, so a binding reads where playback has got to. |
| `Seek(double offset)` | void | Windows has no relative seek, so this sets the position to the current one plus the offset. |
| `Stop()` | void |  |
| `SwitchTo()` | void | Brings the app's window forward. |
| `TogglePlaying()` | void |  |

