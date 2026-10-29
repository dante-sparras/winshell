# Renders the default shell's surfaces and replaces each baseline its render no longer matches.
# Review the changed PNGs in tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines before you commit them.
# -Surface replaces the baselines of one surface only, such as taskbar or quick-settings; the others are still
# compared. See docs/development.md.
param(
    [string]$Surface = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $env:WINSHELL_UPDATE_SNAPSHOTS = if ($Surface) { $Surface } else { '1' }
    dotnet test .\tests\Winshell.Platform.Windows.Tests --nologo --filter 'FullyQualifiedName~Winshell.Platform.Windows.Tests.Snapshots'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    git status --short -- tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines
}
finally {
    Remove-Item Env:WINSHELL_UPDATE_SNAPSHOTS -ErrorAction SilentlyContinue
    Pop-Location
}
