# Winshell.Io

`import Winshell.Io`. The module runs programs and reads their output, reads and writes files, keeps a JSON file as properties, talks over sockets and named pipes, and lets the `winshell ipc` command reach functions in the shell.

The default shell opens its settings window with an `IpcHandler`; see [Winshell Settings](../settings.md) for the `winshell ipc call settings open <page>` command line.

## Process

A program the shell starts and watches, created with `Process { }`. Setting `running` to true starts `command`; `stdout` and `stderr` take a parser that receives the program's output, decoded as UTF-8.

The first word of `command` is looked up on `PATH` with each `PATHEXT` extension, as in a terminal. A `.cmd` or `.bat` file runs through `cmd`, a `.ps1` file through `pwsh` (or Windows PowerShell), and a `.sh` file through `bash`. The program gets no console window.

| Member | Type | Description |
| --- | --- | --- |
| `command` | list of string | The program and its arguments, one per item, such as `["git", "status", "--short"]`. Each item is passed as one argument, so no quoting is needed. |
| `running` | bool | Set it to true to start the command, and to false to stop it. It turns false when the program exits. Declared as `running: true`, the program starts once the object is complete. |
| `environment` | var | Variables to add to the program's environment, as an object such as `({ LANG: "C" })`. A variable set to `null` is removed. Names ignore case, as on Windows. |
| `clearEnvironment` | bool | Starts the program with only the variables in `environment`, not the shell's own. Default `false`. |
| `workingDirectory` | string | The folder the program starts in. Default: winshell's current folder. |
| `stdinEnabled` | bool | Keeps the program's standard input open, so `write()` can send it text. Default `false`, which closes standard input as the program starts. Setting it to false while the program runs closes it then. |
| `stdout` | [DataStreamParser](#datastreamparser) | The parser for standard output: a [StdioCollector](#stdiocollector) or a [SplitParser](#splitparser). Output without a parser is dropped. |
| `stderr` | [DataStreamParser](#datastreamparser) | The parser for standard error. |
| `processId` | int, read-only | The Windows process id while the program runs, and `null` otherwise. |
| `signal(signal)` | method | Stops the program with a Unix signal number. `9` ends it and its child processes at once. `1`, `2` and `15` ask a program with a window to close it, and end a program without one. Other numbers do nothing. |
| `write(data)` | method | Sends text to the program's standard input. Does nothing unless `stdinEnabled` is true and the program runs. |
| `startDetached()` | method | Starts another copy of `command` with the same environment and folder, which the shell does not watch: no output, no signals, and it keeps running after the shell exits. |
| `exec(context)` | method | Stops the running program, then starts a new one. `context` is a command list, or an object with `command`, and optionally `environment`, `clearEnvironment` and `workingDirectory`. Without `environment` or `workingDirectory`, the current ones stay; `clearEnvironment` is false unless the object sets it. |
| `started()` | signal | The program started. |
| `exited(exitCode, exitStatus)` | signal | The program exited with `exitCode`. `exitStatus` is `Process.CrashExit` when the shell ended it by force, and `Process.NormalExit` otherwise. A program that cannot start emits `exited(-1, Process.CrashExit)` and logs a warning in the `process` category. |

Enum values: `NormalExit`, `CrashExit`

Setting `running` to false is the same as `signal(15)`. A program ended by force that way reports `CrashExit`; one ended by `signal(15)`, `signal(2)` or `signal(1)` reports `NormalExit`. When the `Process` is destroyed or the shell reloads, a program still running is ended with its child processes.

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    Process {
        id: whoami
        command: ["whoami"]
        running: true
        stdout: StdioCollector {
            onStreamFinished: console.log("Signed in as", this.text.trim())
        }
        onExited: (exitCode, exitStatus) => {
            if (exitCode !== 0)
                console.warn("whoami failed with", exitCode)
        }
    }
}
```

## StdioCollector

A parser that keeps a whole stream as text, created with `StdioCollector { }` as the value of `Process.stdout`, `Process.stderr` or `Socket.parser`. It has the members of [DataStreamParser](#datastreamparser), but does not emit `read`.

| Member | Type | Description |
| --- | --- | --- |
| `waitForEnd` | bool | Default `true`: `text` changes once, when the stream ends. False updates `text` as each piece arrives. |
| `text` | string, read-only | Everything the stream has sent. A new run of the process keeps the old text until the new output replaces it. |
| `data` | var, read-only | The same text as `text`. |
| `streamFinished()` | signal | The stream ended: the program closed its output, or the socket disconnected. `text` is complete at this point. |

## SplitParser

A parser that emits `read(data)` once for each piece of the stream between markers, created with `SplitParser { }`. By default each piece is one line. It has the members of [DataStreamParser](#datastreamparser).

| Member | Type | Description |
| --- | --- | --- |
| `splitMarker` | string | The text between pieces, which is not part of them. Default `"\n"`; Windows line endings (`\r\n`) count as `\n`. An empty marker emits each chunk as it arrives. Text left over when the stream ends is emitted as a last piece. |

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    Process {
        command: ["ping", "-n", "3", "1.1.1.1"]
        running: true
        stdout: SplitParser {
            onRead: data => console.log("ping:", data)
        }
    }
}
```

## FileView

A file the shell reads, watches and writes, created with `FileView { }`. Its default property is `adapter`, so a [JsonAdapter](#jsonadapter) or a [TomlAdapter](#tomladapter) declared inside it turns the file into properties.

Use a full path, such as one built from `Shell.dataDir`. A relative path is taken from winshell's current folder, not from the config folder. Reads and writes happen off the UI thread unless `blockLoading` or `blockWrites` says otherwise.

| Member | Type | Description |
| --- | --- | --- |
| `path` | string | The file. A `file://` URL works too. Changing it forgets the old text and, with `preload`, reads the new file. |
| `preload` | bool | Reads the file as soon as `path` is set and the object is complete. Default `true`. With false, the file is read on the first `text()` or `reload()`. |
| `blockLoading` | bool | Reads on the UI thread, so the text is there as soon as `path` is set. Default `false`. |
| `blockWrites` | bool | Writes on the UI thread, so a write is finished when the call returns and two quick writes land in order. Default `false`. |
| `blockAllReads` | bool | Makes the first read and `reload()` read on the UI thread. Default `false`. A change of `path` still follows `blockLoading`. |
| `atomicWrites` | bool | Writes a temporary file next to the file, then moves it over the file, so a reader never sees half a write. Default `true`. |
| `printErrors` | bool | Logs a failed read or write as a warning in the `io` category. A missing file is not logged. Default `true`. |
| `watchChanges` | bool | Emits `fileChanged` when the file's text on disk differs from the text this view holds. Default `false`. Its own write does not count, and a hand save in the same moment does. The folder must exist; a write that creates it starts the watch. |
| `loaded` | bool, read-only | Whether the file's text has been read. |
| `adapter` | [FileViewAdapter](#fileviewadapter) | Turns the file's text into properties and back. It reads the text each time the file is read. |
| `text()` | method, returns string | The file's text. If it was not read yet, reads it now. Returns an empty string when the file cannot be read. |
| `data()` | method, returns string | The same text as `text()`. |
| `reload()` | method | Reads the file again. Call it from `onFileChanged` to follow changes on disk. |
| `waitForJob()` | method, returns bool | Returns `true` at once. It does not wait for a read or write in progress. |
| `setText(text)` | method | Replaces the file's contents with `text`. Creates missing folders. |
| `setData(data)` | method | Replaces the file's contents with `data`, as text. |
| `writeAdapter()` | method | Writes what the adapter's properties hold to the file. |
| `loaded()` | signal | The file was read. It fires after each successful read, including `reload()`. |
| `loadFailed(error)` | signal | The file could not be read. `error` is a [FileViewError](#fileviewerror) value, such as `FileView.FileNotFound`. |
| `fileChanged()` | signal | The file changed on disk, with `watchChanges` on. The view does not read it again by itself. |
| `saved()` | signal | A write finished. |
| `saveFailed(error)` | signal | A write failed. `error` is always `FileView.Unknown`. |
| `adapterUpdated()` | signal | A property of the adapter changed, other than by reading the file. Call `writeAdapter()` here to save each change. |

Enum values: `Success`, `Unknown`, `FileNotFound`, `PermissionDenied`, `NotAFile`

## JsonAdapter

The adapter for a JSON file, created with `JsonAdapter { }` inside a [FileView](#fileview). Every property declared on it is a key of the JSON object: reading the file sets each writable property the file has, and `writeAdapter()` writes them all, indented by two spaces. It has the members of [FileViewAdapter](#fileviewadapter).

A key the file does not have keeps its value. A file that is not valid JSON logs a warning in the `io` category and changes nothing. A property whose value is a [JsonObject](#jsonobject) is a nested JSON object.

| Member | Type | Description |
| --- | --- | --- |

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    FileView {
        path: Shell.dataDir + "/clock.json"
        watchChanges: true
        onFileChanged: reload()
        onAdapterUpdated: writeAdapter()
        onLoadFailed: error => {
            if (error === FileView.FileNotFound)
                writeAdapter()
        }

        JsonAdapter {
            id: clock
            property bool showSeconds: false
            property string format: "HH:mm"
            property var timeZones: ["UTC"]
        }
    }
}
```

The file is read at start, written each time a property changes, and read again when another program saves it. When it does not exist yet, the defaults are written.

## TomlAdapter

The adapter for a TOML file, created with `TomlAdapter { }` inside a [FileView](#fileview). Every property declared on it is a key. Reading the file sets each writable property. `writeAdapter()` changes a key only when its value changed, and updates a table, an array of tables, or a dotted key already in the file instead of writing a second one. Comments, blank lines, number spellings, and keys it does not own stay. A key the file does not have yet goes at the end, under the line from `comments` when that key has one. It has the members of [FileViewAdapter](#fileviewadapter).

A key the file does not have keeps its value. A bool property is set only from a TOML boolean or number; a string, array, or table is left as written. An integer past 2^53, or a value that cannot be converted, is left as written. Each of those logs a warning in the `io` category. A file that is not valid TOML logs a warning and changes nothing. A later `writeAdapter()` reads the file again: when that read parses, the change is written into it and keys that were not assigned stay as that read has them; when it does not, or the read fails, nothing is written and a warning is logged.

| Member | Type | Description |
| --- | --- | --- |
| `comments` | object | A comment for each key, as `{ key: "text" }`, written above a key the file does not have yet. A key already in the file keeps the comment it has. |

## JsonObject

A nested JSON object, created with `JsonObject { }` as the value of a property of a [JsonAdapter](#jsonadapter), as in `property JsonObject window: JsonObject { property int width: 400 }`. The properties declared on it are its keys.

A change to a property of a `JsonObject` set directly on the adapter emits `adapterUpdated`. A `JsonObject` inside another `JsonObject` is read and written, but its changes do not emit `adapterUpdated`.

| Member | Type | Description |
| --- | --- | --- |

## IpcHandler

Functions, properties and signals that the `winshell ipc` command can reach from outside the shell, created with `IpcHandler { }`. The functions, properties and signals are the ones declared on the handler.

A function is reachable when its parameters and its return value are annotated with `string`, `int`, `bool`, `real`, `double` or `color`, or `void` for the return value, and it has at most ten parameters. A function without annotations is not reachable. A property of one of those types can be read. Any signal of the handler can be waited for.

| Member | Type | Description |
| --- | --- | --- |
| `enabled` | bool | Whether `winshell ipc` can reach the handler. Default `true`. |
| `target` | string | The name `winshell ipc` uses for this handler. A handler with an empty target is not reachable. When two handlers share a target, the later one wins and a warning is logged in the `ipc` category. |

The commands run against the instance chosen with `-i`, `--pid`, `-p` or `-c`, as `winshell kill` does:

- `winshell ipc show` lists every target with its reachable functions and properties.
- `winshell ipc call <target> <function> [args...]` calls a function and prints what it returns. Each argument is parsed as its parameter's type: an `int` or `real` in invariant culture, a `bool` as `true`, `false` or a number, a `color` as a name or hex, with or without `#`. A `bool` result prints `true` or `false`, and a `color` result prints `#aarrggbb`.
- `winshell ipc prop get <target> <property>` prints a property.
- `winshell ipc wait <target> <signal>` waits for the signal once, prints its first argument, and exits. `winshell ipc listen <target> <signal>` prints the first argument each time the signal fires, until it is stopped. A change signal such as `visibleChanged` works too.

An unknown target, function, property or signal, a wrong number of arguments, or an argument that does not parse prints an error and exits with code 1.

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    IpcHandler {
        id: launcher
        target: "launcher"
        property bool open: false
        signal opened(string query)

        function show(query: string): void {
            launcher.open = true
            launcher.opened(query)
        }

        function toggle(): bool {
            launcher.open = !launcher.open
            return launcher.open
        }
    }
}
```

```powershell
winshell ipc call launcher show "notepad"
winshell ipc call launcher toggle     # prints false
winshell ipc prop get launcher open   # prints false
winshell ipc listen launcher opened   # prints each query
```

## Socket

A stream to a Unix domain socket or a Windows named pipe, created with `Socket { }`, or made by a [SocketServer](#socketserver) for each client. A path that starts with `\\.\pipe\` is a named pipe. It has the members of [DataStream](#datastream): set `parser` to read what arrives.

| Member | Type | Description |
| --- | --- | --- |
| `path` | string | The socket's file path, or a named pipe such as `\\.\pipe\myapp`. Changing it while connected connects to the new path. |
| `connected` | bool | Set it to true to connect and to false to disconnect. It turns true once connected, and false when the other side closes. A named pipe is waited for up to 3 seconds. |
| `write(data)` | method | Sends text, as UTF-8. Text written before the socket connects is sent once it does. |
| `flush()` | method | Sends what is buffered now. |
| `error(error)` | signal | Connecting or writing failed. `error` is `Socket.NotFound` when nothing listens on the path or the pipe did not answer in time, `Socket.ConnectionRefused` for other connect failures, and `Socket.RemoteHostClosed` when a write fails. |

Enum values: `ConnectionRefused`, `RemoteHostClosed`, `NotFound`, `Unknown`

`Unknown` is not emitted on this checkout.

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    Socket {
        id: socket
        path: "\\\\.\\pipe\\myapp"
        connected: true
        parser: SplitParser {
            onRead: data => console.log("myapp:", data)
        }
        onConnectedChanged: if (socket.connected) socket.write("hello\n")
        onError: error => console.warn("myapp is not running:", error)
    }
}
```

## SocketServer

Listens on a Unix domain socket or a named pipe and makes a [Socket](#socket) for each client, created with `SocketServer { }`. Each client's socket is an instance of `handler`, already connected.

| Member | Type | Description |
| --- | --- | --- |
| `path` | string | Where to listen: a file path for a Unix domain socket, or a named pipe such as `\\.\pipe\myshell`. Changing it listens again. A file already at a socket path is deleted first, and the socket file is deleted when the server is destroyed. |
| `active` | bool | Whether the server listens. Default `false`. A path it cannot listen on logs a warning in the `io` category. |
| `handler` | Component | The component made for each client. Its root must be a `Socket`; a client whose handler makes anything else is closed. |

A client's `Socket` stays until the server is destroyed, also after the client disconnects.

## DataStream

The base of [Socket](#socket): a source of text that hands what arrives to a parser. It is not created on its own. Nothing arriving is lost before `parser` is set: it is held and handed over once a parser is there.

| Member | Type | Description |
| --- | --- | --- |
| `parser` | [DataStreamParser](#datastreamparser) | Receives what arrives: a [StdioCollector](#stdiocollector) or a [SplitParser](#splitparser). |

## DataStreamParser

The base of [StdioCollector](#stdiocollector) and [SplitParser](#splitparser), which take a stream's text as it arrives. It is not created on its own; it is reached from `Process.stdout`, `Process.stderr` and `Socket.parser`.

| Member | Type | Description |
| --- | --- | --- |
| `read(data)` | signal | A piece of the stream arrived. `SplitParser` emits it for each piece; `StdioCollector` does not emit it. |

## FileViewAdapter

The base of [JsonAdapter](#jsonadapter) and [TomlAdapter](#tomladapter), which turns a file's text into properties and back. It is not created on its own; it is reached as `FileView.adapter`.

| Member | Type | Description |
| --- | --- | --- |
| `adapterUpdated()` | signal | A property of the adapter changed, other than by reading the file. The `FileView` passes it on as its own `adapterUpdated`. |

## FileViewError

Why a [FileView](#fileview) could not read its file, as `loadFailed(error)` passes it. The same values are on `FileView`, as in `FileView.FileNotFound`.

Values: `Success`, `Unknown`, `FileNotFound`, `PermissionDenied`, `NotAFile`

| Value | Meaning |
| --- | --- |
| `Success` | The file was read. Not passed to `loadFailed`. |
| `Unknown` | Another read or write error. |
| `FileNotFound` | The file or its folder does not exist. |
| `PermissionDenied` | Windows denied access to the file. |
| `NotAFile` | The path is a folder. |
