# Prepares this repo. Git does not run scripts on clone.
# After cloning, run: pwsh scripts/dev.ps1
#
#   pwsh scripts/dev.ps1           clones Quickshell into docs/agents/quickshell and fetches its manual into site/ there
#   pwsh scripts/dev.ps1 -Refresh  downloads the manual again
param([switch]$Refresh)

$ErrorActionPreference = 'Stop'
# This file lives in scripts/. The repo root is the parent of this directory.
$root = Split-Path -Parent $PSScriptRoot

# The hooks are tracked in scripts/hooks. Point git at them.
git -C $root config core.hooksPath scripts/hooks
if ($LASTEXITCODE -ne 0) { throw "git config core.hooksPath failed with $LASTEXITCODE" }
Write-Output "core.hooksPath = scripts/hooks"

$dest = Join-Path $root 'docs/agents/quickshell'
if (-not (Test-Path (Join-Path $dest '.git'))) {
    if (Test-Path $dest) { throw "$dest exists but is not a git checkout" }
    git clone --depth 1 --branch v0.3.2 https://github.com/quickshell-mirror/quickshell.git $dest
    if ($LASTEXITCODE -ne 0) { throw "clone of Quickshell failed with $LASTEXITCODE" }
}
if (-not $Refresh -and (Test-Path (Join-Path $dest 'site/README.md'))) {
    Write-Output 'Manual already present'
    return
}
dotnet run (Join-Path $root 'scripts/fetch-reference.cs') -- --root (Join-Path $dest 'site')
if ($LASTEXITCODE -ne 0) { throw "Quickshell manual fetch failed with $LASTEXITCODE" }
