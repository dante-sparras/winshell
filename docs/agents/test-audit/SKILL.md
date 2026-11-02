---
name: test-audit
description: "Writing, changing, reviewing or sweeping tests: reject duplicate or implementation-coupled proof; remove test-only production seams."
---

# Test Audit

Adapted for .NET and xUnit v3 from [OpenClaw's test-audit skill](https://github.com/openclaw/openclaw/tree/main/.agents/skills/test-audit), MIT. See [LICENSE](LICENSE).

- The authoring gate covers every new or changed test.
- An audit removes low-value proof in coherent owner batches. Deletion counts are not a goal.
- A whole test project or subsystem: read [CAMPAIGN.md](CAMPAIGN.md) first.

## Authoring gate

Answer all four before you add a test. Put the answers in the PR when the test is not an obvious regression test.

1. What observable behavior, invariant or independent contract does it protect?
2. What credible regression turns it red?
3. Why does existing proof miss that regression? Each contract has one primary owner at its strongest boundary. Another layer earns a test only with a distinct transport or lifecycle risk. Extend a `[Theory]` table or a shared fixture first.
4. Does it need an `internal` member, a constructor overload, a flag or a hook that no production code calls? Move the proof to the real boundary.

Boundaries, from the cheapest up:

| Project | Owns |
| --- | --- |
| `Winshell.Services.Tests` | A service against a fake of its Windows backend, on a real `EventQueue` |
| `Winshell.DefaultShell.Tests` | The default shell's models, with no window |
| `Winshell.Shell.Tests` | A real shell with real windows |
| `Winshell.Tests` | `winshell.exe` and shell programs as separate processes |
| `Winshell.Package.Tests` | The packed library and templates, `Category=Package` |

A regression test goes red before the fix for the intended reason and green after it. Say so in the PR, or say why you could not show it. One bug gets one test at its owner, however many layers it crossed. A refactor that keeps behavior leaves tests untouched. Tests wait as [CONTRIBUTING.md](../../../CONTRIBUTING.md#write-tests-that-dont-flake) says.

## Junk patterns

Reject self-comparisons, copied inventories, greps over `src/`, assertions on call shape or private state, duplicate contracts, test-only seams, expected values the subject generated, and fakes that supply the behavior being asserted. A fixture produces only what the real owner would produce. A negative control fails at the guard it names. A test's name matches what it exercises. The full list is in [references/audit.md](references/audit.md#junk-patterns).

## Retention bar

Keep these even when they are static or slow: the public API of the `Winshell` package, the control pipe protocol, CLI output and exit codes, TOML and other files on disk, security guards, platform behavior, defaults, generated templates, the package, the release scripts, and observable ordering. A source inspection stays when it is the cheapest independent guard of something a user sees and survives a rename. A test failing on `main` may be a product bug: repair the owner.

## Audit and validation

- Before you edit, gather the [candidate evidence](references/audit.md#candidate-evidence) and record what proof remains after a deletion.
- Delete an obsolete production seam together with its tests. Assertions keep their strength: a change that makes a test pass by checking less is a defect.
- Run the owner's tests and its siblings with `dotnet test tests/<project> --filter`. Build with `dotnet build .\winshell.slnx -warnaserror`. Tests that open windows follow "Live UI with Winland running" in [AGENTS.md](../../../AGENTS.md).
- Report causes, the contracts you kept, what proof cost, production and test line counts, and what is left.
