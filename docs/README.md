# Docs

The [README](../README.md) installs Winshell, runs the default shell, and says where a configuration lives.

## Guides

- [Configuration guide](configuration.md): write a shell of your own, from one panel up.
- [Troubleshooting](troubleshooting.md): when something doesn't start, show or work.
- [Development](development.md): build Winshell from source, run its checks, and release it.

## Topic pages

- [The taskbar](taskbar.md) and [Winshell Settings](settings.md): the default shell.
- [Notifications](notifications.md), [networking](network.md), [Bluetooth](bluetooth.md), [capture](capture.md), [hotkeys](hotkeys.md) and the [notification area](tray.md): the Windows services a configuration reads.
- [Updates](updates.md): how a portable Winshell updates itself.
- [winshell doctor](doctor.md): what it checks on this PC.

## Reference

- [Reference](reference/index.md): every namespace, type and member a configuration can use.
- [Command line](reference/command-line.md): every `winshell` command.
- [The default shell](reference/default-shell.md): what the built-in shell does.

## Decisions

- [0006](adr/0006-screen-coordinates-keep-the-physical-corner.md): screens keep their physical corner in logical coordinates.
- [0007](adr/0007-windows-first-api.md): Winshell has its own Windows-first API.
- [0008](adr/0008-windows-owns-the-lock-screen.md): Windows owns the lock screen.
- [0009](adr/0009-user-config-files-are-toml.md): user config files are TOML.
- [0010](adr/0010-a-configuration-is-a-winui-program.md): a configuration is a WinUI 3 program.

## Security and policy

- [Threat model](security/threat-model.md)
- [Privacy](privacy.md)
- [Code signing policy](code-signing-policy.md)

`agents/` is for the agents that work on this repo: the C# practices, the test-audit gate, the issue tracker and release notes.
