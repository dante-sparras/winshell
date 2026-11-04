# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

This signing is not active yet. It starts once the SignPath Foundation project for Winshell is approved. Until then, release zips are unsigned. The rest is the policy for when it is.

## What is signed

The Windows binaries built from this repository, in the portable zips on [GitHub Releases](https://github.com/enpaze/winshell/releases) and the winget package made from them: `winshell.exe`, `winshellw.exe`, `winshell.dll`, `Winshell.Services.dll`, `Winshell.Shell.dll` and `Winshell.DefaultShell.dll`, for x64 and arm64. [packaging/signpath/artifact-configuration.xml](https://github.com/enpaze/winshell/blob/main/packaging/signpath/artifact-configuration.xml) lists them and the product name and version SignPath requires of each.

The .NET runtime and the libraries Winshell uses ship in the same zips with their own publishers' signatures, or none. The Microsoft Store package is signed by the Microsoft Store.

## How a release is signed

1. Merging release-please's release PR tags a release and creates a draft. Every change reaches `main` through a pull request that passes CI.
2. GitHub Actions builds the tagged commit on a GitHub-hosted runner (`.github/workflows/package.yml`) and sends the binaries to SignPath, which checks that the build ran in this repository's workflow.
3. An approver approves the signing request in SignPath by hand, for every release. Nothing is signed automatically.
4. The signed binaries are zipped, their hashes checked and uploaded to the draft. Once every required asset verifies, the draft is published.

## Team

- Committers, reviewers and approvers: [Dante Sparrås](https://github.com/dante-sparras)

Every team member uses multi-factor authentication for GitHub and SignPath.

## Privacy

Winshell sends nothing to its developers. See [Privacy](privacy.md).
