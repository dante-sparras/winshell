# Windows owns the lock screen; Winshell only asks it to lock

**Status:** accepted

Winshell is never a lock screen and never looks like one. `Session.lock()` calls `LockWorkStation`, the same request Win+L makes, and Windows' own lock screen takes over. `SessionLock`, `SessionLockSurface`, `SignInContext`, `SignInResult` and `SignInError` are removed, and Winshell no longer asks for, holds, or checks a Windows password. They were ports of Quickshell's `WlSessionLock` and `PamContext`. On Wayland the compositor enforces a session lock. On Windows nothing enforces a lock that Winshell draws: it is an overlay that fails open, and the password prompt it needed is the kind of prompt Windows teaches users not to trust.

## Threat model

The lock protects the signed-in Windows session (open apps, files, browser sessions, tokens) and the account's credential from someone at the keyboard of a PC its user walked away from. Software already running as the user is out of scope for any lock, because it already has the session. It still matters here, because it can switch off a lock that lives in the same desktop.

Windows locks by switching to the Winlogon desktop. LogonUI and the credential providers run there, and no process in the user's desktop can draw on it, read its input, or switch away from it. Winlogon handles Ctrl+Alt+Del itself, and no user-mode hook sees it. The lock screen covers every monitor, accepts Windows Hello (PIN, face, fingerprint), security keys, smart cards, and Microsoft and Entra ID accounts, applies the lockout policy, and offers Narrator, Magnifier and the on-screen keyboard. If the shell crashes after it asks, the session stays locked.

The removed overlay was topmost panels on the user's desktop, a low-level keyboard hook that swallowed Win, Alt+Tab, Alt+Esc, Alt+F4 and Ctrl+Esc, a timer that raised the panels every 500 ms, a hook that called `LockWorkStation` when Ctrl+Alt+Del switched desktops, and `ProcessExit` and `UnhandledException` handlers that did the same. Each of these fails open:

- **Kill.** Any process running as the user can call `TerminateProcess` on Winshell: Task Manager, `taskkill /f`, a scheduled task, a remote shell. No managed handler runs on `TerminateProcess`, a stack overflow, or a fail-fast, so the overlay vanishes and the session is open.
- **Hang.** If the UI thread stalls past `LowLevelHooksTimeout`, Windows skips the keyboard hook, and since Windows 7 removes it without telling the process. The raise timer stops with the thread.
- **Input the hook can't see.** The hook sees keys only. Touch and pen edge gestures, and windows Windows keeps in a higher band than any topmost window, such as accessibility tools and system UI, are not covered.
- **Screens.** A monitor that is plugged in or rearranged is uncovered until the surfaces catch up on a later turn of the loop.
- **Other ways in.** A remote shell, a scheduled task, or anything else that reaches the session without the console never sees the overlay.
- **Ctrl+Alt+Del.** Winlogon takes it first. The overlay only got back to safety by calling `LockWorkStation` from a hook, after the switch, which is Windows' lock with a race in front of it.

Credential handling made it worse:

- The password was typed into an ordinary window on the user's desktop. Any process running as the user can hook the keyboard and read it as it is typed.
- A configuration that looks like the Windows lock screen and asks for the Windows password is indistinguishable from a phishing one. Ctrl+Alt+Del exists so users learn that only Windows asks for that password.
- The password lived in JavaScript and .NET strings that can't be wiped, so it could survive in memory and crash dumps.
- `LogonUserW` started a new interactive logon on each try, wrote a sign-in event to the Security log, and counted toward the lockout threshold. It could not check a Windows Hello PIN, biometrics, a security key, a smart card, or a passwordless account. `SignInResult.MaxTries` was never reported.

Accessibility pulls against the overlay too. Blocking the Win key also blocked Win+Ctrl+Enter (Narrator) and Win+Plus (Magnifier). Letting them through would open more ways around the lock. Windows' lock screen already offers both.

## Considered options

- **Harden the overlay** with a watchdog process, more hooks, `BlockInput`, or a separate desktop made with `CreateDesktop` and `SwitchDesktop`. Each runs as the same user, who can kill it or switch back. It costs an attacker more effort but gives the user no guarantee, and the password problem stays.
- **A credential provider, or replacing the Winlogon shell.** These need an administrator and a system-wide install, and they are the Windows lock screen. The rule above rules them out.

## Consequences

- A configuration that wants a lock button calls `Session.lock()`. It returns whether Windows took the request; the lock happens a moment later, in Windows, and Windows may still not lock, for example when the process is not on the interactive desktop.
- The lock screen's look is set in Windows Settings, under Personalization > Lock screen, not in Winshell.
- Tests use a fake `IWorkstationLock`. Only the `winshell` process registers the real one, so a test can never lock the PC.
- Revisit only if Windows offers a supported way for a third-party lock UI to run on the secure desktop. A lock drawn on the user's desktop stays rejected.
