# QML conformance corpus

Each case is a configuration that exercises one part of the QML language. `ConformanceTests` runs it as a shell, advances time by ten seconds, and compares everything it logged with the `// expect:` lines of its root file, in order.

- A case is a `name.qml` file here, or a `name/` folder with a `shell.qml` and the files it imports.
- `console.log` lines appear as they are printed. Warnings and errors appear as `WARN scene: ...` and `ERROR scene: ...`, with the case's folder removed from paths.

To add a case, write the configuration and its `// expect:` lines from what Qt 6 prints, then make winshell pass it.

## Covered

| Feature | Cases |
| --- | --- |
| Bindings, `Qt.binding`, breaking a binding by assignment | `bindings.qml` |
| Property aliases, to a property, an object, and another alias | `aliases.qml` |
| Signals, handlers, `connect` and `disconnect`, `Connections`, change signals | `signals.qml` |
| File types, inline components, `Component`, `createObject`, `destroy`, `Loader`, `Qt.createComponent`, required properties | `components/`, `loader-errors/` |
| `Repeater` over a count, an array and a `ListModel` | `repeater.qml` |
| States, `PropertyChanges` by target and by id, transitions | `states.qml` |
| JavaScript imports, `.pragma library`, `.import`, directory and `config` imports | `scripts/` |
| Enums, default and list properties, `readonly`, `Behavior`, `Qt.callLater` | `objects/` |
| Errors name the file and line: bindings, handlers, scripts, binding loops, unknown types and properties, syntax errors | `error-*.qml`, `error-in-script/`, `loader-errors/` |

## Not supported

These differ from Qt, and no case expects the Qt behavior:

- A value in `PropertyChanges` is set when its state applies. An expression there is not kept as a binding while the state is active.
- A transition animates entering a state. The values a state restores when it is left are set without animating.
- `Loader.asynchronous` loads synchronously.
- A script imported with `.import` inside another script is one copy for the whole shell, even without `.pragma library`.
