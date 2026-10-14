# Settings

Winshell keeps `%USERPROFILE%\.config\winshell`.

| File | Role |
| --- | --- |
| `settings.json` | The bar, which plugins load, the bar's edge, `"extensions"` (the DLLs Winshell loads), and `"taskbar": "hidden"` or `"shown"`. Shown is the default |
| `plugins\<id>\manifest.json` | A plugin you added. Built-ins stay in `plugins` beside the program |

A file may start with `"include": ["path", ...]`. The paths are relative to that file. The included files are read in order and merged: objects merge, a list or a single value replaces, and `null` removes a key. Your file applies last. A missing include is logged and skipped. With no file, the shell starts from its own defaults and shows the Windows taskbar.

Winoma ships the settings it wants you to start from as `%LOCALAPPDATA%\Winoma\defaults\winshell`. That file lists `..\Winoma\Winoma.Desktop.dll` under `extensions` and hides the taskbar.

## The bar

Drag a bar item to move it. Press it, pull it along the bar, and an accent line shows where it will land: between two items, at either end of a section, or in an empty section. Let go and the bar is written to `settings.json`. Pull the item well away from the bar and let go to leave it where it was. A click that does not travel still opens the panel.

Right-click the bar itself, anywhere that is not an item, for the bar's settings. Position moves the bar to the top, bottom, left, or right edge of the screen, and the panel opens again there. A second right-click closes the panel. Dragging empty bar space toward an edge also moves the bar. A double click on that empty space toggles the bar's transparency.
