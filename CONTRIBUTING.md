# Contributing

The [development runbook](docs/development.md) covers the prerequisites, the first checkout, and every command. Run `pwsh scripts/quick-check.ps1` before every push.

## Write tests that don't flake

A test waits on a real signal: an event, a task's completion, or a fake clock.

- **No sleeps or fixed timeouts as a way to wait.** A timeout can stop a test that hung, but a passing test must never depend on how long something took. `Thread.Sleep` and `Task.Delay` don't build in `tests/`. When nothing gives a signal, such as a file or another process, use `Wait.Until` from `tests/TestSupport/Wait.cs`.
- **Only `Wait.ForNothingToHappen` waits for a set time.** Use it when a check is that something did not happen. A slow machine can then miss a bug, but it can't fail a test that should pass.
- **Pass `TestContext.Current.CancellationToken` to anything that waits.** It is cancelled when a test has run for two minutes, so the test fails alone and the run goes on.
- **Time comes through a `TimeProvider`.** `DateTime.Now`, `Stopwatch` and their relatives don't build in `src/`. A test passes a `FakeTimeProvider`, given a UTC time. `TimeProvider.System` is for what only real time can drive: a file's write time, a Windows object, the CLI's own process.
- **Use the real desktop only in a `Category=Slow` test.** A fast test depends on no window, running app, device or setting of the PC it runs on. `Wait.Until` is for what really gives no signal: another process, or a window.
- **Don't depend on a free thread pool worker.** A busy pool can hold back queued work for seconds, so anything that has to happen on time needs its own thread.
- **Test the default shell's logic without a window.** Its view models are plain C#, tested in `Winshell.DefaultShell.Tests`. A test class that opens a WinUI window or starts `winshell.exe` carries `[Trait("Category", "Slow")]`.

Nothing retries a test and nothing sets one aside. A test that fails sometimes is a bug in the test or in Winshell, so find the cause and fix it, on `main` as much as in a PR. Tests are built so that they can't flake:

- **Each test owns what it uses.** It has its own temp folder and leaves no global state behind. A test that needs something one at a time, such as the keyboard hook, joins an xUnit collection with the others that do.
- **Order and timing are shaken on every run.** Test assemblies run side by side, classes run in parallel on twice as many threads as cores, and the order changes each time. Coupling between tests fails at once. The run prints its seed, and `pwsh scripts/run-tests.ps1 -Tier <tier> -Seed <seed>` plays each assembly's order back.
- **A time limit only catches a hang.** A test claims nothing about how fast something is on this machine today, so set a limit far above what the work takes under load: only the defect it guards can miss it.
- **A changed test proves itself.** On a PR, each changed test class runs 20 times, or 5 times for slow tests, each in another order. A failure in any run, or runs that didn't all run the same tests, fails the PR. A PR that changes more than 30 test classes is a sweep, and skips this. `pwsh scripts/repeat-tests.ps1 -Tests <paths>` does the same here for fast tests, given paths separated by commas.

## Open a PR

### Branch name

`<type>/<short-description>`, lowercase, using a type from the table below. For example, `fix/duplicate-orders` or `feat/csv-export`.

### PR title

A [Conventional Commit](https://www.conventionalcommits.org) in plain language: `<type>(<optional scope>): <what changed>`, starting lowercase. For example, `fix(api): retries no longer duplicate orders`.

| Type | Use it for |
| --- | --- |
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation only |
| `style` | Formatting, with no change in behavior |
| `refactor` | Restructuring code, with no change in behavior |
| `perf` | A performance improvement |
| `test` | Adding or fixing tests |
| `build` | The build system or dependencies |
| `ci` | CI configuration |
| `chore` | Maintenance that fits nowhere else |
| `revert` | Undoing an earlier change |

For a breaking change, add `!` before the colon: `feat(api)!: remove the v1 endpoints`.

Commits on your branch can use any message; the squash merge replaces them with the PR title. Using the same format for them is still a good habit.

CI checks the PR title. It also checks the branch name, but only as a reminder; it doesn't block merging.

### PR description

Fill in the PR template:

- **Summary**: the issue it comes from, if any, and the smallest diagram, diff sketch, or tree that shows the change.
- **Evidence**: a before and after, from the tests or manual steps you ran. "Tests pass" on its own isn't enough. Add **Not verified** for anything you couldn't check. Don't commit evidence to `main`.
- **Proof for the type of change**: the `pr-rules` check reads these from your title, body and files.
  - `fix` adds a test that failed before the fix, and says how under **Before:** and **After:**. If it can't have a test, say why on a `**No test:**` line. A fix to docs alone doesn't need one.
  - `perf` gives a number under **Before:** and under **After:**, or in a table with those headings.
  - A `feat`, `fix` or `perf` change to XAML in `src/` shows a `winshell inspect` screenshot. `pwsh scripts/pr-screenshot.ps1 <png>` publishes one and prints the Markdown to paste. If nothing visible changed, say why on a `**Not visual:**` line.
  - Other types carry no extra proof.
- **Merge Danger**: whether this is a one-way or two-way **Door**, and its **Blast Radius**, the one word for what it can affect.

If an AI agent did the work, end the description with the model and the tool you used, such as "Claude Opus 5.5 in Claude Code".
