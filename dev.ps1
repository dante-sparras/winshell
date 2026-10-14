# Prepares this repo for development. Git does not run scripts on clone.
# After cloning, run: pwsh dev.ps1
#
#   pwsh dev.ps1           logs NuGet in to GitHub Packages with the gh login, clones Quickshell into
#                          docs/agents/quickshell, and fetches its manual into site/ there
#   pwsh dev.ps1 -Refresh  downloads the manual again
#   pwsh dev.ps1 -Local    packs the winland checkout beside this one into %USERPROFILE%\.nuget\local-feed and builds
#                          against it, so a Winland fix can be tried here before it is released (ADR 0177)
#   pwsh dev.ps1 -Pinned   builds against the Winland version Directory.Build.props pins again
param([switch]$Refresh, [switch]$Local, [switch]$Pinned)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$feed = 'https://nuget.pkg.github.com/dante-sparras/index.json'
$localFeed = Join-Path $env:USERPROFILE '.nuget\local-feed'
$userConfig = Join-Path $env:APPDATA 'NuGet\NuGet.Config'
$localProps = Join-Path $root 'local.props'

# The feed's login lives in the user's NuGet config, never in the repo. Windows keeps the password encrypted there.
$token = (& gh auth token).Trim()
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'gh is not logged in. Run gh auth login, then gh auth refresh -s read:packages' }
& dotnet nuget remove source github-dante-sparras --configfile $userConfig *> $null
& dotnet nuget add source $feed -n github-dante-sparras -u dante-sparras -p $token --configfile $userConfig | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet nuget add source failed' }

if ($Pinned) {
    Remove-Item $localProps -ErrorAction SilentlyContinue
    Write-Output 'Building against the pinned Winland'
    return
}
if ($Local) {
    $winland = Join-Path (Split-Path $root) 'winland'
    if (-not (Test-Path (Join-Path $winland 'pack.ps1'))) { throw "No winland checkout at $winland" }
    $version = (& pwsh -NoProfile -File (Join-Path $winland 'pack.ps1') -Out $localFeed | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'pack.ps1 in winland failed' }
    & dotnet nuget remove source local-feed --configfile $userConfig *> $null
    & dotnet nuget add source $localFeed -n local-feed --configfile $userConfig | Out-Null
    Set-Content $localProps "<Project>`n  <PropertyGroup>`n    <WinlandVersion>$version</WinlandVersion>`n  </PropertyGroup>`n</Project>`n" -NoNewline
    Write-Output "Building against Winland $version from $winland"
    return
}

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
python -c "import markdownify, bs4" 2>$null
if ($LASTEXITCODE -ne 0) {
    python -m pip install markdownify beautifulsoup4
    if ($LASTEXITCODE -ne 0) { throw 'markdownify and beautifulsoup4 are required' }
}
python (Join-Path $root 'docs/fetch_reference.py') --site quickshell --root (Join-Path $dest 'site')
if ($LASTEXITCODE -ne 0) { throw "Quickshell manual fetch failed with $LASTEXITCODE" }
