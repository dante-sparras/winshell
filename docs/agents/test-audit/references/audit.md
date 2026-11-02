# Audit workflow

Use this after the [authoring gate](../SKILL.md#authoring-gate). A whole project also needs [CAMPAIGN.md](../CAMPAIGN.md).

## Junk patterns

Reject new matches unless the [retention bar](#retention-bar) names an independent contract. Audit existing matches:

- Tests with no assertion, self-comparisons, identity copiers, expected values the subject generated.
- Copied inventories and lists of names; `File.ReadAllText` greps over `src/`; a fixed list of a public surface that must be edited with each new member.
- Tests of a private helper that repeat what its public boundary already proves; one shared helper replayed by each caller.
- `internal` members, constructor overloads, static resets and `InternalsVisibleTo` entries that only tests use; production paths only tests call.
- Fakes that implement the asserted behavior, or that pretend two Windows APIs match.
- Fixtures that hand over a result, an order or an acknowledgement the owner should produce; a persistence assertion against a path the code never writes.
- A capability flag asserted in place of the behavior it promises.
- Negative controls that pass on an unrelated guard, or on a rejection the code never reaches.
- `Wait.ForNothingToHappen` as the only check, where a positive signal exists.
- Names that promise behavior the inputs and assertions never exercise.

## Retention bar

- Keep independent contracts: public API, control pipe protocol, CLI output, TOML and other files on disk, security, platform, defaults, templates, package, release, and observable ordering with a credible regression.
- A source inspection is valid when it is the cheapest independent guard of a user-facing key, byte or path and survives a rename.
- Static or slow is no reason to delete. A baseline failure may be a product bug: reproduce it and repair the owner.

## Candidate evidence

Before you edit, read the full test, the production owner, its callers and callees, sibling tests, overlapping proof, and `git log` for why it exists. For a claim about a dependency, read that dependency's source or types.

Record for each candidate:

- its exact name and location, and the failure it can detect;
- the non-test callers of the production code or seam it covers;
- the stronger proof that remains, or why none is needed;
- why it exists, and what its deletion unlocks;
- the risk, and the command that validates the change.

No evidence, no deletion. Discovery is read-only: report candidates before edits, and prefer a few high-confidence ones. Delete obsolete seams and dead paths, move retained regressions to their owner, and prefer a change with fewer production lines. Uncertainty stays in the tree.

## Validation and handoff

- Run the owner's and siblings' tests. For a removed source grep, exercise the code path it stood in for.
- When tests go, look for support files, fakes and `internal` members they orphaned.
- Check `git diff --numstat` and `git diff --check`.
- Report causes, removed categories and seams, false positives you kept, proof and its cost, production and test lines, the PR's state, and what is left.
