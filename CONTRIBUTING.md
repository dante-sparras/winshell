# Contributing

## Before you start

- **Found a bug?** [Open an issue](https://github.com/dante-sparras/winshell/issues/new/choose).
- **Have an idea?** [Open a feature request](https://github.com/dante-sparras/winshell/issues/new?template=feature_request.yml).
- **Search first.** Someone may already have reported it, or be working on it.
- **Found a vulnerability?** Don't open an issue; follow [SECURITY.md](.github/SECURITY.md).

Before you build a feature or change how something behaves, open a feature request and wait for the maintainer to agree in it. Link that issue in your PR. A small fix for an obvious bug needs no prior issue; just explain the bug in the PR.

## Set up

The [development runbook](docs/development.md#first-checkout) covers the prerequisites, the first checkout, environment setup, and every command.

## Make your change

**One problem per PR.** If you fixed two unrelated things, open two PRs. A cleanup or refactor you noticed along the way gets its own PR too, unless the fix depends on it.

**Include everything that problem needs**: the code, its tests, and any docs that would otherwise be wrong.

**Check your change** before you open the PR. Build with warnings as errors and run the focused tests for what you changed. Add the Corpus stage when you touch the parser, Leak when you touch reload, and Performance when you touch Jint; the [commands](docs/development.md#commands) list them. `pwsh scripts/check.ps1` runs everything. The required `ci` job runs its `-Quick` part on a pull request, and all of it on a push to `main` and on the release pull request, so a failure there holds back the release. The ruleset also requires `installer`, `actionlint`, `pr-title`, and the docs `build` job.

## Write tests that don't flake

A test waits on a real signal: an event, a task's completion, or a fake clock.

- **No sleeps or fixed timeouts as a way to wait.** A timeout can stop a test that hung, but a passing test must never depend on how long something took. `Thread.Sleep` and `Task.Delay` don't build in `tests/`. When nothing gives a signal, such as a file or another process, use `Wait.Until` from `tests/TestSupport/Wait.cs`.
- **Only `Wait.ForNothingToHappen` waits for a set time.** Use it when a check is that something did not happen. A slow machine can then miss a bug, but it can't fail a test that should pass.
- **Pass `TestContext.Current.CancellationToken` to anything that waits.** It is cancelled when a test has run for two minutes, so the test fails alone and the run goes on.
- **Time comes through a `TimeProvider`.** `DateTime.Now`, `Stopwatch` and their relatives don't build in `src/`. Code in an engine reads `QmlEngine.Time`, and a test sets it with a `FakeTimeProvider` service, given a UTC time. `TimeProvider.System` is for what only real time can drive: a file's write time, a Windows object, the CLI's own process.
- **Don't depend on a free thread pool worker.** A busy pool can hold back queued work for seconds, so anything that has to happen on time needs its own thread.

## Flaky tests

The `ci` job runs a failed test once more. A failed test that passes on that rerun is flaky:

- The job passes, and shows a warning naming the test.
- The `flaky-report` job files an issue labeled `flaky`, or comments on the one already open for that test.

A test that fails twice fails the job. So does a test run that crashed, and more than 10 failed tests, which are not rerun.

To quarantine a flaky test while its cause is found, add its fully qualified name to [`tests/quarantine.txt`](tests/quarantine.txt) in a PR, add it to the [quarantine checklist](https://github.com/dante-sparras/winshell/issues/960), and use `Refs #` for its `flaky` issue. `Fixes #` would close that issue with the test still broken. `ci` then leaves the test out, and the `quarantine` job runs it on `main` without blocking anything. In the PR that fixes the test, remove the line and tick the checklist. Quarantine covers the main test stage, not the leak, corpus or performance tests.

Don't skip or delete a flaky test to get a PR green.

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
- **Evidence**: a before and after, from the tests or manual steps you ran. "Tests pass" on its own isn't enough. Add **Not verified** for anything you couldn't check. For a change to the settings or notification surfaces, link the `settings-surface` or `notification-surface` artifact from the PR's `ci` run. Don't commit evidence to the repo.
- **Merge Danger**: whether this is a one-way or two-way **Door**, and its **Blast Radius**, the one word for what it can affect.

If an AI agent did the work, end the description with the model and the tool you used, such as "Claude Opus 5.5 in Claude Code".

## Review and merge

- Every change reaches `main` through a PR, and the required checks (`ci`, `installer`, `actionlint`, the docs `build` job, and the PR title) must pass first.
- PRs are squash-merged. Your PR title becomes the commit message on `main`, so make it readable on its own.
- Releases are cut from those commits, so your PR title is also your line in the changelog.
- A PR that doesn't follow this guide may be closed with a note saying what's missing. Fix it and ask for another look, or open a new PR.
