#requires -Version 7.0
param(
    [string] $Tag,
    [string] $PackageDirectory = 'packages',
    [string] $Repository = $env:GH_REPO
)

function Publish-WinshellRelease {
    param(
        [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string] $Tag,
        [Parameter(Mandatory)][string] $PackageDirectory,
        [Parameter(Mandatory)][string] $Repository
    )
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $version = $Tag.Substring(1)
    $names = @(
        "winshell-$version-win-x64.zip", "winshell-$version-win-arm64.zip",
        'SHA256SUMS.txt', 'winget-manifests.zip', 'install.ps1', 'uninstall.ps1'
    )
    $files = foreach ($name in $names) {
        $path = Join-Path $PackageDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing release asset: $name" }
        [pscustomobject]@{ Name = $name; Path = $path; Digest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $release = gh api "repos/$Repository/releases/tags/$Tag" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Could not read release $Tag." }
    # Preflight every existing digest before uploading anything. Published releases are never repaired in place.
    $missing = @(
        foreach ($file in $files) {
            $existing = @($release.assets | Where-Object name -CEQ $file.Name)
            if ($existing.Count -eq 0) { $file; continue }
            if ($existing.Count -ne 1 -or $existing[0].digest -cne $file.Digest) {
                throw "$($file.Name) already exists with a different digest. Leaving $Tag unchanged."
            }
        }
    )
    if (-not $release.draft) {
        if ($missing.Count -gt 0) {
            throw "$Tag is published and lacks release assets. It cannot be repaired by this workflow; publish a later approved version through a draft."
        }
        Write-Output "$Tag is published already, with all expected hashes. Nothing changed."
        return
    }
    foreach ($file in $missing) {
        gh release upload $Tag $file.Path --repo $Repository
        if ($LASTEXITCODE -ne 0) { throw "Could not upload $($file.Name). $Tag remains a draft." }
    }
    # Re-read GitHub's digests: a successful upload command alone is not permission to freeze the release.
    $uploaded = gh api "repos/$Repository/releases/tags/$Tag" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $uploaded.draft) { throw "Could not verify draft $Tag before publication." }
    foreach ($file in $files) {
        $asset = @($uploaded.assets | Where-Object name -CEQ $file.Name)
        if ($asset.Count -ne 1 -or $asset[0].digest -cne $file.Digest) {
            throw "$($file.Name) is missing or has a different digest. $Tag remains a draft."
        }
    }
    if ($env:SIGNPATH_ORGANIZATION_ID -and $uploaded.body -notmatch 'Code signing policy') {
        $notes = "$($uploaded.body)`n`n[Code signing policy](https://github.com/$Repository/blob/main/docs/code-signing-policy.md)"
        gh release edit $Tag --repo $Repository --notes $notes
        if ($LASTEXITCODE -ne 0) { throw "Could not update the draft's signing notes. $Tag remains a draft." }
    }
    gh release edit $Tag --repo $Repository --draft=false
    if ($LASTEXITCODE -ne 0) { throw "Could not publish $Tag. Its uploaded files remain on the draft." }
}

if ($MyInvocation.InvocationName -ne '.') {
    Publish-WinshellRelease -Tag $Tag -PackageDirectory $PackageDirectory -Repository $Repository
}
