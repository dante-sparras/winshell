# Test-pruning campaign

Campaign mode prunes one owner's whole test surface: a test project, or one area such as tray or Winland. The value bar, retention bar and evidence in [SKILL.md](SKILL.md) apply to every lane. This file adds the order of work. Each step ends on its completion criterion, so finish one before you start the next.

## 1. Baseline

Record the test and support line counts and every test file's pass or fail state at a pinned `main` SHA. Keep baseline failures in their own list, because they may be real bugs.

Done when every test file in scope has a recorded result.

## 2. Lanes and inventory

Split the surface into lanes along production owner boundaries, not file names. `Winshell.Services.Tests` has lanes such as apps, audio, bluetooth, crash reports, doctor, hotkeys, IPC, media, network, notifications, TOML, tray, updates, windows and Winland. Include the owner's shared fixtures and fakes.

Done when every test file belongs to exactly one lane.

## 3. Ledger per lane

Read every test in the lane in full, with its `[Theory]` data, and the production owner, its callers and its history. Give each test one mark. A `[Theory]` is one test unless its rows need different marks.

- `R`: retain, naming the contract and the bug it catches.
- `F`: retain the contract and repair the assertion, such as a vacuous negative.
- `C`: consolidate, naming the owner that absorbs the assertion first.
- `D`: delete, naming the proof that remains, or why there is no contract.

Judge a test by its assertions. Its name may promise more than it checks.

Done when every test in the lane has a mark and one line of evidence.

## 4. Layer plan per lane

Use the ledger as input. Look for the redundant layer: suites that replay one contract through a fake around a stronger suite at the real boundary. Name the keeper for each contract. Prefer the real boundary with a fake Windows backend over a fake collaborator. Correct ledger errors this pass finds.

Done when each lane names its retired files, its keepers, the assertions to carry into keepers, and the test-only seams this unlocks.

## 5. Cutover

Edit lane by lane, in small PRs. Change shared fixtures in `tests/TestSupport` from one PR at a time. With each lane, remove the seams it unlocks: constructor overloads, `internal` getters, reset methods, `InternalsVisibleTo` entries.

Done when every lane plan is applied and each lane's keepers pass.

## 6. Preservation review

Before you claim completion, compare deleted coverage with the keepers. Look for contracts that lost their only proof, and for new assertions that cannot fail. For each restored or moved contract, make one deliberate mutation of the production owner, confirm the keeper goes red, and restore the file.

Done when every gap is restored or rejected with source evidence, and each moved contract has a caught mutation.

## 7. Product defects

A baseline failure that survives into a keeper is a bug report. Fix it at its owner in its own PR, and show it failing without the fix. Note unrelated discrepancies for later, outside the campaign's PRs.

Done when each repaired defect has a red control and a green fix on the same test.

## 8. Reconcile and hand off

Merge `main` into a long campaign branch. When `main` changed a file the campaign deleted, keep the deletion and port the new contract into the keeper. Hand off with the [SKILL.md](SKILL.md) report, plus:

- baseline and final test, support and production line counts;
- lanes, retired layers and keepers;
- preservation gaps and their mutations;
- product defects with their red and green runs.
