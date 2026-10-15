# Copy the post-merge hook into this checkout. The installed file is LF-only.
#   pwsh -NoProfile -File scripts/install-post-merge.ps1
$ErrorActionPreference = 'Stop'

$hooks = git rev-parse --git-path hooks
if ($LASTEXITCODE -ne 0) { throw 'This directory is not a git checkout.' }

$source = Join-Path $PSScriptRoot 'hooks/post-merge'
if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source" }

$bytes = [System.IO.File]::ReadAllBytes($source)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) { $text = $text.Substring(1) }
$text = $text.Replace("`r`n", "`n").Replace("`r", "`n")

$dest = Join-Path $hooks 'post-merge'
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($dest, $text, $utf8)

$chmod = Get-Command chmod -ErrorAction SilentlyContinue
if ($chmod) {
    & $chmod.Source +x $dest
    if ($LASTEXITCODE -ne 0) { throw "chmod +x failed for $dest" }
}

Write-Output "Installed $dest"
