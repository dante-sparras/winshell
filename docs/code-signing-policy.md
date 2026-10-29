# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

This signing is not active yet. It starts once the SignPath Foundation project for Winshell is approved. Until then, release zips are not signed with this certificate. The steps below are the policy for when that project is set up.

## What is signed

The Windows binaries built from this repository, in the portable zips on [GitHub Releases](https://github.com/dante-sparras/winshell/releases) and the winget package made from them: `winshell.exe`, `winshellw.exe` (the same program without a console window), `winshell.dll`, `Winshell.Qml.dll`, `Winshell.Runtime.dll` and `Winshell.Platform.Windows.dll`, for x64 and arm64. Each one is named Winshell and carries the release's version.

The .NET runtime and the libraries Winshell uses (SkiaSharp, HarfBuzzSharp, Jint, Vortice and others) ship in the same zips with their own publishers' signatures, or none. They are not signed with this certificate. The Microsoft Store package is signed by the Microsoft Store.

## How a release is signed

1. A release is tagged and created as a draft when the maintainer merges release-please's release PR. Every change reaches `main` through a pull request that passes CI ([CONTRIBUTING.md](https://github.com/dante-sparras/winshell/blob/main/CONTRIBUTING.md)). The maintainer's practice is to review a pull request from someone who is not a committer before merging it. GitHub does not require that approving review.
2. GitHub Actions builds the tagged commit on a GitHub-hosted runner (`.github/workflows/package.yml`) and sends the binaries to SignPath. SignPath checks that the build ran in this repository's workflow.
3. An approver approves the signing request in SignPath by hand, for every release. Nothing is signed automatically.
4. The signed binaries are zipped, their hashes checked, and uploaded to the draft. After every required asset's remote hash verifies, the draft is published and its assets become immutable.

[packaging/signpath/artifact-configuration.xml](https://github.com/dante-sparras/winshell/blob/main/packaging/signpath/artifact-configuration.xml) is the list of files SignPath signs, and the product name and version it requires of each.

## Team

- Committers and reviewers: [Dante Sparrås](https://github.com/dante-sparras)
- Approvers: [Dante Sparrås](https://github.com/dante-sparras)

Every team member uses multi-factor authentication for GitHub and SignPath.

## Privacy

Winshell sends nothing to its developers. A portable Winshell checks GitHub for updates, which you can turn off in Winshell Settings > About. See the [privacy policy](privacy.md).
