# User config files are TOML

**Status:** accepted

A file Winshell keeps for the user to read or edit by hand is TOML: `preferences.toml` (Winshell Settings), `launcher.toml` (Start pins) and `quick-settings.toml` (the quick settings tiles). They were JSON. JSON has no comments, so a file couldn't explain its own keys, and a trailing comma or a missing quote made the whole file unreadable. TOML has comments and is written one `key = value` per line, which suits a flat list of settings.

The default shell reads and writes them through `TomlAdapter` in `Winshell.Io`, which works like `JsonAdapter`. To write, it parses the file into [Tomlyn](https://github.com/xoofx/Tomlyn)'s syntax tree, replaces the values of the keys it has properties for, and adds the keys the file is missing at the end of its top-level keys, each under its comment. Everything else in the file stays: the user's comments, blank lines, key order, and keys and tables Winshell doesn't know. A file that isn't valid TOML is not read. A later write reads the file again. If that read parses, only values assigned since the last good read are written into it, and the other keys stay as that read has them. If it does not parse, or the read fails, nothing is written and a warning is logged.

There were no users yet, so the switch has no migration: Winshell never reads the old `.json` names.

## Considered options

- **Keep JSON, or JSON with comments.** Comments in JSON aren't standard. Editors flag them, and hand edits still break on commas.
- **Write the whole file from the values each time.** It is simpler, but it throws away whatever the user wrote, and the Tomlyn model API drops comments.
- **YAML.** Its indentation and implicit types (`no` reads as false) are easy to get wrong by hand, for a file that is a flat list.

## Consequences

- Tomlyn (BSD-2-Clause, the license Jint already has) is a dependency of `Winshell.Services`.
- TOML has no null. `TomlAdapter` leaves out a key whose value is null or undefined, and takes it out of the file. The comments around it stay.
- `JsonAdapter` stays for configurations that want JSON. App state that nobody edits by hand, such as the taskbar's `taskbar.json` in the data folder, isn't covered by this rule.
- A new file a user edits by hand is TOML, through `TomlAdapter`.
