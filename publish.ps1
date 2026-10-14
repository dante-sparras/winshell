# Builds Winshell into one folder, the way it installs (ADR 0176):
#
#   <Out>\Winshell   Winshell.Host.exe, Winshell.Watch.exe, winshell.exe, version.json
#
# version.json names the commit, the version, and the Winland version this shell speaks to (ADR 0177). winshell.exe is
# native code, which needs Visual Studio's C++ build tools. NativeAOT finds them through vswhere.exe, so its folder goes
# on the PATH here.
param(
    [string]$Out = (Join-Path $env:TEMP 'winshell-publish'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rid = 'win-x64'
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $installer) { $env:PATH = "$installer;$env:PATH" }
$version = & (Join-Path $root 'version.ps1')

function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments -join ' ') failed with $LASTEXITCODE" }
}

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
$winshell = Join-Path $Out 'Winshell'
$shell = Join-Path $root 'src\Winshell\Winshell.csproj'
$winland = (& dotnet msbuild $shell -getProperty:WinlandVersion -nologo).Trim()

Write-Host "Winshell $version on Winland $winland..."
Invoke-Dotnet @('publish', $shell, '-c', $Configuration, '-r', $rid, '-o', $winshell, "-p:Version=$version", '--nologo')
# The native client is published on its own and only its exe is kept: the managed copy's dll and json would be read instead.
$native = Join-Path $Out '.native\winshell'
Invoke-Dotnet @('publish', (Join-Path $root 'src\Winshell.Cli\Winshell.Cli.csproj'), '-c', $Configuration, '-r', $rid, '-o', $native, "-p:Version=$version", '--nologo')
Copy-Item (Join-Path $native 'winshell.exe') $winshell -Force
Remove-Item (Join-Path $winshell 'winshell.dll'), (Join-Path $winshell 'winshell.deps.json'), (Join-Path $winshell 'winshell.runtimeconfig.json') -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $Out '.native')

$commit = (& git -C $root rev-parse HEAD).Trim()
$date = (& git -C $root show -s --format=%cI HEAD).Trim()
Set-Content (Join-Path $winshell 'version.json') ('{"commit":"' + $commit + '","date":"' + $date + '","version":"' + $version + '","winland":"' + $winland + '"}') -NoNewline -Encoding utf8NoBOM

foreach ($file in 'Winshell.Host.exe', 'Winshell.Watch.exe', 'winshell.exe', 'Winland.Ipc.dll', 'Winshell.Kit.dll') {
    if (-not (Test-Path (Join-Path $winshell $file))) { throw "The publish has no $file" }
}

Write-Host "Published to $Out"
