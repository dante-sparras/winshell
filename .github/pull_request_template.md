<!--
Read CONTRIBUTING.md before opening a PR. Solve one underlying problem.
Title: a conventional commit in plain language, such as "fix(api): retries no longer duplicate orders".
Branch: <type>/<short-description>, such as "fix/duplicate-orders", or agent/<tool>/<issue#>-<slug> for an AI agent.
-->

## Problem

<!-- The problem in a sentence or two: what happens, what should happen, and how to reproduce it where that helps. -->

## Change

<!-- How this solves it. Show the smallest view that makes the point: a diagram, a diff sketch, or a tree. Keep prose
brief. If it spans several areas, say why the one fix needs each. Split independent fixes into their own PRs. -->

## Scope and approval

Fixes #

<!-- The issue it fixes after "Fixes #". With no issue, delete that line and say why this needs none, such as a small fix
of an obvious bug. -->

## Verification

<!-- What you checked for the changed behavior, how, and what you saw. Name the tests or manual steps and their results;
"tests pass" is not verification. Repo-wide checks you ran for every PR need no mention.
Upload evidence to GitHub; never commit PR-only assets to main.
A fix adds a test and says here how it failed before the fix, or says why it can't have one on a "**No test:**" line.
A perf change gives a number for each, here or in a table with Before and After headings.
A feat, fix or perf change to XAML in src/ shows a winshell inspect screenshot, or says why not on a "**Not visual:**"
line. pwsh scripts/pr-screenshot.ps1 <png> publishes one and prints the Markdown to paste. -->

- **Before:**
  **After:**

**Not verified:** <!-- Anything you could not check. -->

## Merge Danger

**Door:** <!-- One-way or two-way: can this be walked back? -->

**Blast Radius:** <!-- One word for what this can affect, then any ramifications. -->

**Agent:** <!-- If an AI agent wrote this PR, the model and the harness, such as "Claude Opus 5.5 in Claude Code".
Otherwise delete this line. -->
