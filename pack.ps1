# Packs Winshell.Core and Winshell.Kit (ADR 0177).
#
#   pwsh pack.ps1                 packs into %USERPROFILE%\.nuget\local-feed, which Winoma's dev.ps1 -Local reads
#   pwsh pack.ps1 -Out <folder>   packs there. CI packs into a folder and pushes to GitHub Packages
param(
    [string]$Out = (Join-Path $env:USERPROFILE '.nuget\local-feed'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = & (Join-Path $root 'version.ps1')
New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($project in 'Winshell.Core', 'Winshell.Kit') {
    & dotnet pack (Join-Path $root "src\$project\$project.csproj") -c $Configuration -o $Out "-p:Version=$version" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack $project failed with $LASTEXITCODE" }
}
Write-Output $version
