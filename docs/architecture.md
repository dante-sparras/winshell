# Architecture

Winshell is a modular monolith: one program, with assembly boundaries for shared infrastructure and folder boundaries for service features. These are the decided target rules. The migration below introduces them in independently buildable steps; the build enforces the [sealed interop](#sealed-interop) boundary, and the other boundaries are not all enforced yet.

## Dependency direction

The production libraries follow this order, from their consumers toward their dependencies:

```text
Winshell.DefaultShell -> Winshell.Shell -> Winshell.Services -> Winshell.Core -> Winshell.Native
```

A project may reference a lower layer directly when it needs that layer's contract. It must never reference a higher layer or form a cycle. For example, Shell can call a safe Native window operation without adding a forwarding method to Services. Native references no other Winshell project. The CLI composes these libraries and owns process supervision; the package project assembles their distribution.

| Owner | Responsibility |
| --- | --- |
| `Winshell.Native` | Native declarations, COM contracts, unsafe interop, marshaling, native resource ownership and safe managed operations. |
| `Winshell.Core` | `IEventLoop`/`EventQueue`, `Log`, `ObservableObject`, TOML and Crashes. Shared infrastructure has no dependency on a service feature or WinUI. |
| `Winshell.Services` | One assembly of feature folders. Services apply policy and publish state on the event loop; feature backends perform platform work. |
| `Winshell.Shell` | WinUI hosting, window lifecycle and composition through the existing public Shell API. |
| `Winshell.DefaultShell` | The default desktop's views, models and preferences. |
| CLI (`src/Winshell`) | Commands, configuration builds and shell process supervision. |

Keep the existing public Shell API. Moving an implementation does not justify compatibility shims, forwarding assemblies or duplicate implementations. Keep shared infrastructure in Core and feature policy in its owning Services folder.

## Sealed interop

Only `Winshell.Native` may contain `LibraryImport`, `DllImport`, `ComImport` or unsafe interop. This restriction applies to every other project, including tests. Other assemblies consume managed contracts that do not require pointers, unmanaged function pointers or unsafe callers.

Adopt CsWin32 when introducing Native. Generate Win32 declarations there from an explicit API list, and remove the handwritten declarations they replace. Convert the remaining `DllImport` declarations during this migration. Native may own declarations CsWin32 cannot supply, such as undocumented interfaces; do not maintain duplicate Win32 declarations alongside generated ones.

Keep allocation and release together: handles, COM objects, buffers and callbacks have an explicit owner and lifetime. Callback exceptions must not escape into native code. Put safe operations at this boundary; do not expose the generated declaration set as a public service API or move feature policy into Native.

The build enforces this. The root `Directory.Build.targets` adds `BannedSymbols.Interop.txt` to every project except Native, so `DllImport`, `LibraryImport`, `ComImport`, `GeneratedComInterface` and `GeneratedComClass` fail with RS0030, and it forces `AllowUnsafeBlocks` off, so unsafe code fails with CS0227. It runs after each project file, so a project can't turn either guard back on, and a new project under `src/` or `tests/` is covered without edits. Native keeps the clock and sleep bans the other projects have. Do not exempt a consumer, suppress the diagnostics or exclude generated consumer code to bypass the boundary.

The one exception is the Windows App SDK's `UndockedRegFreeWinRT-AutoInitializer.cs`, which its package adds to each self-contained WinUI executable. It is package startup code we don't own, not our interop, so only that file, matched by its path in the NuGet package folder, drops the ban. Any `[DllImport]` of ours in the same project still fails.

## Services and feature ownership

Services remains one assembly. Each feature folder owns its `I*Backend` seam and platform implementation. Complete the missing seams for Windows, Tray, Apps, Winland and Session, and retain the useful seams already present elsewhere. A seam describes platform operations the production service actually needs; avoid interfaces added only for tests.

Only a feature may consume its own backend contracts and implementations. Another feature uses the service's managed contract, events or shared Core infrastructure. It must not construct, resolve, reference or call a neighboring feature's backend. Composition belongs with the feature that owns the backend.

Test service policy against fake backends on a real `EventQueue`. Fakes supply platform results and signals; the real service supplies the behavior under test. Keep tests that own distinct platform, transport or lifecycle contracts. Preserve assertion strength when moving tests to their new owner.

## Shell and default desktop

Split `HostWindow` by responsibility, with explicit owners for window lifetime, placement, native message handling and presentation. Thin out `Shell.cs` and `ShellServices.cs` so they compose focused types instead of collecting feature behavior. Preserve disposal order, event-loop affinity, cancellation and the existing public Shell API.

Use CommunityToolkit.Mvvm source generators only in DefaultShell and only where they remove clear property or command boilerplate. Keep model behavior and notification ordering explicit. Core's `ObservableObject` remains shared infrastructure; do not spread a UI generator dependency through the lower layers.

## Crash supervision

The CLI supervises a shell in a child process. An unexpected shell exit triggers respawn with backoff; a requested stop ends supervision. Keep restart policy, cancellation and child ownership in a focused supervisor rather than in the window host. Drive policy time through `TimeProvider` so tests can advance it without sleeping.

The supervisor owns Explorer taskbar recovery independently of the child. Restore the taskbar after a child exits, before waiting or respawning, after a launch failure, and when supervision stops. Recovery must run even if the shell cannot execute its cleanup. A new child acquires its own taskbar state; a restart must not inherit a stale hidden-taskbar lease. Verify recovery across crashes, repeated failures and intentional shutdown.

## Distribution and configuration

Keep one `Winshell` library NuGet package. Include Native and Core alongside Shell and Services as those assemblies are introduced; a configuration must not acquire references to separately published module packages. The existing template package remains the delivery mechanism for `dotnet new` templates. Keep package and template contracts covered as assembly ownership changes.

User configuration stays TOML. Keep its file format, defaults and semantics through the refactor. Do not add a second configuration system or compatibility layer.

Remove the orphan Qml, Runtime and Platform.Windows `bin`/`obj` trees. Verify each resolved deletion target is one of those build-output directories before deleting recursively; this cleanup does not authorize deleting source or another checkout's build outputs.

## Architecture proof

Add `Winshell.Architecture.Tests` to the normal fast checks. Use NetArchTest or project-reference and compiled dependency guards to enforce downward dependencies and prevent cross-feature backend use. Guard the actual project graph and backend consumers, including implementations, rather than maintaining a list of expected source files. A renamed or newly added backend must still be covered.

The compiler/analyzer owns the interop ban; architecture tests own the assembly and feature boundaries. Demonstrate that each guard rejects a representative forbidden dependency or declaration, then remove that violation. Do not add production hooks solely to exercise the guards.

Tests follow the installed `test-audit` skill's gate, [where a test belongs](../CONTRIBUTING.md#where-a-test-belongs) and the [test rules](../CONTRIBUTING.md#write-tests-that-dont-flake). Never weaken a test, skip a failure or add flaky retries. Use fake clocks and real completion signals. A refactor that retains behavior retains its existing proof.

## Migration and delivery

Deliver in dependency order, with the rules documented first:

1. Introduce Native with CsWin32 and package integration. Migrate interop and its callers in focused, independently buildable groups, converting the remaining `DllImport` declarations. Remove each replaced declaration in the same PR. Seal the boundary with build enforcement once the last consumer has moved; finish Native before starting the higher-layer restructuring.
2. Extract Core and include it in the single package. Move infrastructure and its existing tests without changing behavior. Add architecture tests as the relevant boundaries become enforceable, and complete the full graph guard when all layers exist.
3. Complete the Services feature seams and their fake-based tests. Split HostWindow and thin Shell composition while preserving its public API. These can proceed side by side only when their files and shared types do not overlap.
4. Apply useful DefaultShell MVVM generators and add CLI crash supervision with taskbar recovery. Remove the named orphan build trees. Keep independent changes in separate PRs and serialize any shared build or package edits.
5. Verify the complete dependency graph, interop ban, feature isolation, package contents, TOML behavior and recovery contracts. Confirm every migration PR has merged.

Each small PR owns one issue, thread, worktree and branch. Record its allowed paths, prerequisites and acceptance checks in its issue. Prefer [PR stacks](development.md#pull-request-stacks) for dependent layers; each layer must still build and be reviewable on its own. An intermediate migration step is not permission to add new interop outside Native or expand an old boundary violation.

Before parallel work, inspect open PR files. Only one PR at a time changes a shared build file, solution, package version list, banned-symbol list, lockfile, test support file or shared type. Follow the [conflict rules](../AGENTS.md#conflicts-and-merging): rebase onto the current base, resolve by intent, preserve both behaviors, and escalate collisions in the same behavior instead of guessing. Re-run checks after rebasing.

Run the [checks for the change](development.md#checks-by-change) before each push, including applicable slow and package checks. Use free CI runners only. For live UI work, follow the [Winland procedure](development.md#live-ui-with-winland-running), inspect the shell with `winshell inspect`, view its screenshot and check its tree. Never inject real user input for verification.

Review each diff against these rules, AGENTS.md and the test-audit gate before opening its PR. Enable auto-merge for standalone PRs; merge a ready stack with the stack workflow. The author owns failing checks, cancelled runs and conflicts until GitHub reports every opened PR as `MERGED`. Report anything that could not be verified.
