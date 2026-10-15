# Releasing

This repo holds a hello-world app and a reference checkout. Nothing is released from it.

When a release is wanted, copy these from winland and adjust the artifact names:

- `scripts/release.ps1`
- `.github/workflows/stable-release.yml`
- `docs/releasing.md`

Tags matching `v*`, `rc.*`, and `abandoned-rc.*` stay locked by `.github/rulesets/protect-release-tags.json`. An admin can create them. Nothing in this repo pushes a receipt tag, so the file has no Actions-app bypass.
