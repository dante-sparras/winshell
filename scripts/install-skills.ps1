#requires -Version 7.0
# Installs every skill skills-lock.json records, for Codex and Claude Code, as copies. See docs/development.md#agent-skills.
#
#   pwsh scripts/install-skills.ps1
#
# Copies, not links: on Windows the skills CLI's links fail quietly, and its experimental_install then leaves the
# external skills linked to no agent. Each source installs at the ref the lock pins, so the lock does not change.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$skills = 'skills@1.7.4'
$agents = @('-a', 'codex', 'claude-code', '--copy', '-y')

Push-Location $root
try {
    $lock = (Get-Content -Raw -LiteralPath 'skills-lock.json' | ConvertFrom-Json -AsHashtable).skills
    $sources = $lock.GetEnumerator() | Group-Object { $_.Value.source + '#' + $_.Value['ref'] }
    foreach ($source in $sources) {
        $first = $source.Group[0].Value
        $package = if ($first['ref']) { "$($first.source)#$($first.ref)" } else { $first.source }
        $names = @($source.Group | ForEach-Object { $_.Key } | Sort-Object)
        Write-Host "$package : $($names -join ', ')"
        npx -y $skills add $package -s @names @agents
        if ($LASTEXITCODE -ne 0) { throw "Installing $package failed." }
    }
} finally {
    Pop-Location
}
