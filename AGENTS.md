# Winshell

A desktop shell toolkit for Windows 11. A configuration is a .NET project that builds into one program.

## Stack

- Windows 11. Projects target `net10.0-windows10.0.22621.0`. The template package targets `net10.0`.
- .NET 10 SDK, pinned in `global.json`. `Directory.Build.props` treats warnings as errors.
- C# and WinUI 3. `winshell.exe` runs the default shell and builds a configuration.
- xUnit v3 for `tests/`, through VSTest.
- PowerShell 7 for `scripts/`. `scripts/install.ps1` and `scripts/uninstall.ps1` also run on Windows PowerShell 5.1.

For the folder map, see [Project structure](docs/development.md#project-structure).

## Parallel work

Many agents work on this repo at once.

- First, in every session, run `git fetch origin main` and `git rev-list --left-right --count HEAD...origin/main`. A second count above 0 means this checkout, and the `AGENTS.md` you just read, are behind `main`: update the base as [Start a task](docs/development.md#start-a-task) describes before you edit or rely on them.
- One task is one thread, one issue, one worktree, one branch and one small PR. If the task has no issue, file one with the fields of the Agent task form in `.github/ISSUE_TEMPLATE/agent_task.yml` and its `area:*` labels.
- Name the branch `agent/<tool>/<issue#>-<slug>`, such as `agent/claude/123-tray-overflow`, and put `Fixes #<issue#>` in the PR body.
- Run tasks side by side, and assign them to parallel threads, only when their areas and files don't overlap. `gh pr list --json number,headRefName,files` shows what open PRs touch.
- One PR at a time changes a shared file: a `Directory.*.props` such as `Directory.Packages.props`, `global.json`, `nuget.config`, `.editorconfig`, `winshell.slnx`, `src/BannedSymbols.txt`, `tests/TestSupport/`, a lockfile, or a type more than one module uses.
- Prefer a stack of small, focused PRs for related work, using `gh stack` (`gh extension install github/gh-stack`). Each layer still owns its issue and worktree; follow the [PR stack workflow](docs/development.md#pull-request-stacks).
- Use `gh stack init <bottom-branch> [<next-branch> ...]` to adopt branches, `gh stack add <next-branch>` to add a layer, and `gh stack submit` to publish the stack. Check its state with `gh stack view`; update it with `gh stack rebase`, the required checks in each affected worktree, then `gh stack push`.

### Conflicts and merging

`main` has no merge queue and doesn't require a PR to be up to date, so two PRs that each pass can break `main` together.

1. When your PR conflicts with `main`, rebase onto the latest `origin/main` (a stacked PR runs `gh stack rebase`); never merge `main` into your branch. Force-push only your own PR branch, with `git push --force-with-lease origin HEAD`, never `main`.
2. Resolve each conflict by intent: find the PR that changed the same code with `git log --oneline <your-branch>..origin/main -- <file>`, read it, and keep both behaviors. Taking "ours" or "theirs" wholesale is never a resolution.
3. If both sides change the same behavior, run `git rebase --abort`, comment on your PR naming the other PR and the collision, and escalate to the lead thread, or to the user if there is none, instead of guessing.
4. Run the [checks for the change](docs/development.md#checks-by-change) again before you push, even after a clean rebase: it can still break logic.
5. You own the PR until it merges: fix every failing check, re-run a cancelled one with `gh run rerun <run-id>`, and confirm `gh pr view <number> --json state --jq .state` prints `MERGED`. For a standalone PR, enable auto-merge with `gh pr merge <number> --squash --auto`.
6. For a stack, wait until every PR meets its merge requirements, then merge the whole stack with `gh stack merge <stack-number> --squash` as one operation to avoid intermediate rebases and repeated CI runs.

## Before you finish

- Writing, changing or reviewing a test? It must pass the gate in [skills/test-audit/SKILL.md](skills/test-audit/SKILL.md).
- Before every push, run the [checks for the change](docs/development.md#checks-by-change). Prose-only changes need `pwsh -NoProfile scripts/check.ps1 -Lint`. Pages containing or removing C# examples, and generated reference pages, also need the targeted `DocsTests` command there. Other changes need `pwsh scripts/check.ps1`: build with 0 warnings, formatting, fast .NET tests and PowerShell tooling tests, with no window opened.
- Changed what `[Trait("Category", "Slow")]` tests cover? Run them too: `pwsh scripts/check.ps1 -Tier Slow`, following [the live UI procedure](docs/development.md#live-ui-with-winland-running). `dotnet test .\tests\Winshell.Package.Tests --filter Category=Package` packs a shell from each template and runs it; the main stage leaves it out. The Package workflow runs it, not required, on PRs that change `templates/` or the release files.
- CI runs the remaining checks, so leave `package.ps1` and arm64 to it. The required `ci` job checks Markdown links offline and selects prose-only, compiled-documentation or full checks on PRs and pushes to `main`. The required `slow` job skips documentation-only changes. The ruleset also requires `installer`, `actionlint`, `pr-title` and `pr-rules`.
- After changing Markdown or workflows, run `pwsh -NoProfile scripts/check.ps1 -Lint` immediately. If you touched `scripts/`, `tests/Scripts/` or `.github/`, also run the jobs [docs/development.md](docs/development.md#commands) lists for that change.
- Before opening a PR, get a fresh review: a subagent, or a second pass of your own, reads the diff against this file and the test-audit gate. Fix what it finds and review again, until it finds nothing.
- Title the PR and fill in the PR template as [CONTRIBUTING.md](CONTRIBUTING.md#open-a-pr) describes. The `pr-rules` job holds each type of PR to its proof: a `fix` adds a test that failed before the fix, a `perf` gives before and after numbers, and a XAML change shows a `winshell inspect` screenshot.
- Say what you couldn't verify.
- Changed a public member of `Winshell.Shell` or `Winshell.Services`? Update its `///` comment, run `dotnet run scripts/generate-reference.cs`, and commit the pages it writes.
- Added or changed a command, prerequisite, environment variable, or top-level folder? Update [docs/development.md](docs/development.md) or its project structure section.
- Don't edit `.agents/skills/` or `.claude/skills/`; the `skills` CLI overwrites them.
- Never commit `.env` files or credentials.

## Live UI with Winland running

Before running slow tests, a shell, or packaging that opens windows, follow [Live UI with Winland running](docs/development.md#live-ui-with-winland-running). Run one live session at a time, use an unused monitor, and restore it in `finally`. With one monitor, ask before changing the user's workspace.

For a visual change, follow [See what you changed](docs/development.md#see-what-you-changed): inspect the running shell, view its screenshot, and check the tree.

## Agent skills

Install the project skills as described in [Development](docs/development.md#agent-skills). Installed copies in `.agents/skills/` and `.claude/skills/` are ignored; edit project-owned sources in `skills/`.

### Issue tracker

Issues live in GitHub Issues for enpaze/winshell, used through `gh`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Test audit

Audits and sweeps of `tests/` follow `skills/test-audit/SKILL.md`. It is this repo's own, adapted from OpenClaw's skill.

### Domain docs

One `GLOSSARY.md` at the repo root. See `docs/agents/domain.md`.

## Notes

I like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. Try to honor the dev's intent in both a minimal and realistic fashion.
