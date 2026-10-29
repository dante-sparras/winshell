# Imports actual renders from a CI run's snapshot-diffs artifact as snapshot baselines.
# Review the changed PNGs in tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines before you commit them.
# -Surface imports one surface only, such as taskbar, quick-settings or notice. See docs/development.md.
param(
    [long]$FromRun,
    [ValidateSet('taskbar', 'start', 'quick-settings', 'notification-center', 'osd', 'settings', 'notice')]
    [string]$Surface
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($FromRun -le 0) {
    throw 'Use -FromRun <CI run id> to import the snapshot-diffs artifact. Local renders depend on this PC''s text settings.'
}
$root = Split-Path -Parent $PSScriptRoot
$download = Join-Path ([IO.Path]::GetTempPath()) ('winshell-snapshots-' + [guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    gh run download $FromRun --repo dante-sparras/winshell --name snapshot-diffs --dir $download
    if ($LASTEXITCODE -ne 0) { throw "Could not download snapshot-diffs from run $FromRun." }
    $renders = @(Get-ChildItem -LiteralPath $download -Filter '*.actual.png' -File -Recurse | Where-Object {
        $_.Name -cmatch '^(taskbar|start|quick-settings|notification-center|osd|settings|notice)-.+\.actual\.png$' -and
        (!$Surface -or $_.Name.StartsWith($Surface + '-', [StringComparison]::Ordinal))
    })
    if ($renders.Count -eq 0) { throw 'The artifact has no actual renders for the requested surface.' }
    $baselines = Join-Path $root 'tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines'
    New-Item -ItemType Directory -Force -Path $baselines | Out-Null
    foreach ($render in $renders) {
        $name = $render.Name.Substring(0, $render.Name.Length - '.actual.png'.Length) + '.png'
        Copy-Item -LiteralPath $render.FullName -Destination (Join-Path $baselines $name)
    }
    Write-Host "Imported $($renders.Count) baselines from CI run $FromRun. Review the PNG changes before committing."
    git status --short -- tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedDownload = [IO.Path]::GetFullPath($download)
    if (!$resolvedDownload.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Temporary download is outside the temp directory: $resolvedDownload"
    }
    if (Test-Path -LiteralPath $resolvedDownload) { Remove-Item -LiteralPath $resolvedDownload -Recurse -Force }
    Pop-Location
}
