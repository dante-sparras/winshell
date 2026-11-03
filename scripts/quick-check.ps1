# What to run before a push: the build, with warnings as errors, the formatting and the fast tests. It takes under two
# minutes and opens no window, so it is safe beside your own windows under Winland. CI runs the rest: see
# docs/development.md.
& (Join-Path $PSScriptRoot 'check.ps1') -Quick -Tier Fast
exit $LASTEXITCODE
