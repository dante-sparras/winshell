#requires -Version 7.0
# Decides which checks a change needs, from GitHub changed-file JSON read at the checkout root. stdout is ready for
# GITHUB_OUTPUT. pr-rules.ps1 dot-sources this file for Test-Prose.
param(
    [string] $Files,
    [ValidateRange(1, 3000)][int] $MaxFiles = 3000
)

# Prose is what no build, test or package reads.
function Test-Prose {
    param([string] $Path)
    $Path -match '^docs/|\.md$'
}

if ($MyInvocation.InvocationName -eq '.') { return }

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
$nonProse = @($paths | Where-Object { -not (Test-Prose $_) })
function Test-Changed {
    param([string] $Pattern)
    $unknown -or @($nonProse -match $Pattern).Count -gt 0
}
$checks = [ordered]@{
    code = $unknown -or $nonProse.Count -gt 0
    docs = $unknown
    scripts = Test-Changed '^scripts/|^tests/Scripts/|^\.github/(workflows|actions)/'
    workflows = Test-Changed '^\.github/(workflows|actions)/'
    # What package.yml's jobs read: the release files, the template, the library's project and build files, the package
    # tests and the support they compile, and the scripts and actions that pack, package and test them.
    package = Test-Changed (
        '^(packaging|templates|src/Winshell\.Package|tests/(Winshell\.Package\.Tests|TestSupport)|scripts/lib|\.github/actions)/',
        '^src/[^/]+/[^/]+\.csproj$',
        '^((src|tests)/)?Directory\.',
        '^(global\.json|nuget\.config|BannedSymbols\.Interop\.txt|release-please-config\.json|\.release-please-manifest\.json)$',
        '^scripts/(check|pack|package|publish-release)\.ps1$',
        '^\.github/workflows/(package|release-please)\.yml$' -join '|')
}

if (-not $checks.code) {
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
foreach ($name in $checks.Keys) {
    "$name=$($checks[$name].ToString().ToLowerInvariant())"
}
