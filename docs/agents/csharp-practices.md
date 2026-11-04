# C# practices

How C# is written here, and what to fix when you find it. The build already enforces much of this through `Directory.Build.props`, `.editorconfig`, `src/BannedSymbols.txt` and the threading analyzers; this page covers what the build cannot see. Apply it to the code you touch. A refactor that applies it to code nobody is touching is its own PR.

## Decisions already made

These were open questions; they are settled, so build on them rather than reopening them in a PR.

- **.NET 10 and C# 14.** No C# 15 features (unions, `closed`) until the projects target `net11.0`.
- **No framework packages for what the repo already has.** Logging is `Winshell.Log`, change notification is `Winshell.ObservableObject`, and services are created by hand at the composition root (`Shell.Run`, the default shell's `Program`). There is no `Microsoft.Extensions.*` DI or logging, and no MVVM toolkit.
- **Interop is hand-written, not CsWin32.** A P/Invoke is `[LibraryImport]`; the only `[DllImport]`s left return a `[ComImport]` interface, which `[LibraryImport]` cannot marshal. A COM interface is a `[ComImport]` interface declared in `src`, never one from `System.Runtime.InteropServices.ComTypes` (banned: a trimmed release calls the wrong method), and stays `[ComImport]`: moving one to `[GeneratedComInterface]` changes how its objects are created and cast. Direct3D and DXGI go through Vortice. Interop sits next to the feature that calls it, or in `Winshell.Services/Native` when several features share it. In `Winshell.Services` and `Winshell.Shell`, a function, struct or constant is declared once: when a second feature needs one that a file declares privately, move it to `Win32` rather than copy it.
- **Expected outcomes are small result records** (a parse that returns the value and its diagnostics); failures are exceptions. No general `Result<T>` type.
- **xUnit v3 on VSTest** for every test project, with the rules in [CONTRIBUTING.md](../../CONTRIBUTING.md#write-tests-that-dont-flake).
- **No StyleCop or Roslynator.** The built-in analyzers at `Recommended` are the rule set.
- **No native AOT for the shell.** Releases are self-contained, trimmed and ReadyToRun; code stays trim-safe (no reflection discovery, no reflection JSON).

## Structure

- **A folder is a feature** (`Tray/`, `Network/`, `Taskbar/`). A file holds one top-level type, named after it; a few small records that only make sense together may share a file.
- **Name a type after what it is** (`TrayHost`, `MonitorSize`). `Manager`, `Helper`, `Utils`, `Common` and `Misc` name nothing: move each member to the feature that uses it.
- **One reason to change per class.** If its summary needs "and", split it. Past about 400 lines, look for the second job.
- **A class is `sealed` and `internal`** unless something outside its assembly uses it. The public types of `Winshell.Shell` and `Winshell.Services` are the `Winshell` package's API, documented in `docs/reference/`. `Winshell.DefaultShell`'s views and models are public and the `winshell-default` template copies them into a configuration's project; their visibility is not a change to make in passing.
- **An interface earns its place** with a second implementation or a test fake. Otherwise use the class.
- **Side effects live at the edge.** Win32, files, the clock and processes sit behind a thin type a test can replace; the logic that decides what to do stays pure.
- **Delete what nothing uses**: dead code, commented-out code, a parameter every caller passes the same way, a layer that only forwards.

## Code

- **Records for data, `required init` for shapes with many members.** A record that holds a collection, `ImmutableArray<T>` included, compares it by reference; write `Equals` and `GetHashCode` with `SequenceEqual` when equality matters.
- **Primary constructors for small classes.** Their parameters are mutable captured state: never reassign one, and don't also copy it to a field (CS9124). A type with invariants to check gets a normal constructor.
- **`!` carries a comment saying why it is safe.** Usually the fix is the flow, or `[NotNullWhen]`/`[MemberNotNull]`.
- **Pattern matching and `switch` expressions** for dispatch over a closed set; leave out the discard arm when the compiler can prove the match exhaustive.
- **JSON goes through a `JsonSerializerContext`, and regexes are `[GeneratedRegex]`.** A new lookup table built once and read often can be frozen (`ToFrozenDictionary`).
- **No `#region`, `dynamic` or `goto`, and no nested ternaries.**

## Async and threads

- **Async all the way down.** `async void` is for UI event handlers only, with the body in a `try` that logs. An `async` lambda never goes to an `Action`.
- **Wait without blocking.** `.Result`, `.Wait()` and `.GetAwaiter().GetResult()` block a thread that may be the UI thread or a pool worker the work itself needs. Where a sync API is unavoidable (a COM callback, a WinRT call at startup), say so in a comment.
- **A `CancellationToken` is the last parameter** of anything that waits or does I/O, and is passed all the way down.
- **Library code (`Winshell.Services`) uses `ConfigureAwait(false)`**; UI code resumes on its dispatcher and does not.
- **The UI thread is reached through its `DispatcherQueue`**; check `TryEnqueue`'s result, which is false during shutdown. `Window.Current`, `CoreDispatcher` and `DependencyObject.Dispatcher` are UWP leftovers that compile and return null in a desktop app.
- **A task nobody awaits catches and reports its own failure** inside the method it starts; only then is `_ = DoAsync()` safe.
- **Event-driven over polling.** A timer that ticks when nothing changed costs an always-running shell idle CPU; schedule to the next boundary or hook the OS event.

## Native resources and events

- **Every handle has an owner that releases it deterministically**: a `SafeHandle` or a `Dispose` that runs on a known path. Finalizers are a backstop, never the plan.
- **A delegate passed to native code is rooted in a field** for as long as the hook lives.
- **Every `+=` on a longer-lived source has its `-=`**, on window close or dispose.

## Errors and logs

- **Catch at a boundary**: a UI handler, one control's update (so one bad control can't take the shell down), the CLI's command, the global handlers. Everywhere else, let it rise.
- **A catch logs or handles.** Rethrow with `throw;`, or wrap with the original as `inner`.
- **`TryParse` over a `try` around `Parse`** on any path that runs often.
- **Throw the specific built-in exception.** A custom exception type exists only if something catches it.
- **Validate at the edge** (CLI args, TOML, the control pipe, Win32 return values) and trust non-nullable parameters inside.
