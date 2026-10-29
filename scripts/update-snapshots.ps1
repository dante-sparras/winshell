# Renders the default shell's surfaces and replaces each baseline its render no longer matches.
# Review the changed PNGs in tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines before you commit them.
# -Surface replaces the baselines of one surface only, such as taskbar, quick-settings or notice; the others
# are still compared. The script runs only on the CI runner. See docs/development.md.
param(
    [ValidateSet('taskbar', 'start', 'quick-settings', 'notification-center', 'osd', 'settings', 'notice')]
    [string]$Surface
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Text uses the machine's ClearType and Windows build. Baselines match the CI runner, so a
# local update writes PNGs that CI rejects. Take the pixels from that run's snapshot-diffs artifact.
if ($env:GITHUB_ACTIONS -ne 'true') {
    throw "Refusing to update snapshots off the CI runner. Download the snapshot-diffs artifact from the failed run. See docs/development.md."
}
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
