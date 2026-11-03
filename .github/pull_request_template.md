<!--
Read CONTRIBUTING.md before opening a PR. Solve one underlying problem.
Title: a conventional commit in plain language, such as "fix(api): retries no longer duplicate orders".
Branch: <type>/<short-description>, such as "fix/duplicate-orders".
-->

## Summary

<!-- What changed and why. Link the issue it fixes, if there is one.

Show the smallest view that makes the point: a diagram, a diff sketch, or a tree. Keep prose brief. -->

## Evidence

<!-- A before and after: the tests or manual checks you ran and what you observed. "Tests pass" alone is not enough.
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

<!-- If an AI agent did the work, end with the model and the tool you used, such as "Claude Opus 5.5 in Claude Code". -->
