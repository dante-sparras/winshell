# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

The Windows binaries built from this repository, in the portable zips on [GitHub Releases](https://github.com/dante-sparras/winshell/releases) and the winget package made from them: `winshell.exe`, `winshell.dll`, `Winshell.Qml.dll`, `Winshell.Runtime.dll` and `Winshell.Platform.Windows.dll`, for x64 and arm64. Each one is named Winshell and carries the release's version.

The .NET runtime and the libraries Winshell uses (SkiaSharp, HarfBuzzSharp, Jint, Vortice and others) ship in the same zips with their own publishers' signatures, or none. They are not signed with this certificate. The Microsoft Store package is signed by the Microsoft Store.

## How a release is signed

1. A release is tagged when the maintainer merges release-please's release PR. Every change reaches `main` through a reviewed pull request ([CONTRIBUTING.md](../CONTRIBUTING.md)).
2. GitHub Actions builds the tagged commit on a GitHub-hosted runner (`.github/workflows/package.yml`) and sends the binaries to SignPath. SignPath checks that the build ran in this repository's workflow.
3. An approver approves the signing request in SignPath by hand, for every release. Nothing is signed automatically.
4. The signed binaries are zipped, their hashes checked, and uploaded to the release.

[packaging/signpath/artifact-configuration.xml](../packaging/signpath/artifact-configuration.xml) is the list of files SignPath signs, and the product name and version it requires of each.

## Team

- Committers and reviewers: [Dante Sparrås](https://github.com/dante-sparras), and members of the repository with write access
- Approvers: [Dante Sparrås](https://github.com/dante-sparras)

Every team member uses multi-factor authentication for GitHub and SignPath.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. The one exception is the update check of the portable build: it asks GitHub for the latest release, and you can turn it off in Winshell Settings > About. See the [privacy policy](privacy.md).
