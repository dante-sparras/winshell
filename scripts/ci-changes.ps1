#requires -Version 7.0
# Classifies GitHub changed-file JSON from the checkout root; stdout is ready for GITHUB_OUTPUT.
param(
    [Parameter(Mandatory)][string] $Files,
    [ValidateRange(1, 3000)][int] $MaxFiles = 3000,
    [switch] $Draft
)

$ErrorActionPreference = 'Stop'
$filesChanged = @(Get-Content -LiteralPath $Files -Raw | ConvertFrom-Json -AsHashtable)
$unknown = $filesChanged.Count -eq 0 -or $filesChanged.Count -ge $MaxFiles
$paths = @(
    foreach ($file in $filesChanged) {
        if (-not $file['filename']) { $unknown = $true }
        $file['filename']
        if ($file['previous_filename']) { $file['previous_filename'] }
    }
)
$nonDocs = @($paths | Where-Object { $_ -notmatch '^docs/|\.md$' })
$code = $unknown -or $nonDocs.Count -gt 0
$checks = [ordered]@{
    fast = $code
    code = $code
    docs = $unknown
    scripts = $unknown -or @($nonDocs | Where-Object { $_ -match '^scripts/|^tests/Scripts/|^\.github/(workflows|actions)/' }).Count -gt 0
    workflows = $unknown -or @($nonDocs | Where-Object { $_ -match '^\.github/(workflows|actions)/' }).Count -gt 0
}

if (-not $code) {
    foreach ($file in $filesChanged) {
        $names = @($file['filename'], $file['previous_filename']) | Where-Object { $_ }
        if (@($names | Where-Object { $_ -match '^docs/reference/winshell.*\.md$' }).Count -gt 0) {
            $checks.docs = $true
            break
        }
        # These are the pages DocsTests reads. A removed fence can disappear from the checkout, so inspect the patch too.
        $pages = @($names | Where-Object { $_ -eq 'README.md' -or ($_ -match '^docs/.*\.md$' -and $_ -notmatch '^docs/agents/') })
        if ($pages.Count -eq 0) { continue }
        if ($null -eq $file['patch'] -or $file['patch'].Contains('```csharp')) {
            $checks.docs = $true
            break
        }
        foreach ($page in $names) {
            if ((Test-Path -LiteralPath $page -PathType Leaf) -and [IO.File]::ReadAllText((Join-Path (Get-Location) $page)).Contains('```csharp')) {
                $checks.docs = $true
                break
            }
        }
        if ($checks.docs) { break }
    }
}
if ($Draft) {
    $checks.fast = $false
    $checks.code = $false
    $checks.docs = $false
}
foreach ($name in $checks.Keys) {
    "$name=$($checks[$name].ToString().ToLowerInvariant())"
}
