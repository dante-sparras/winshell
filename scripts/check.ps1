# Builds winshell.slnx with warnings as errors, then runs the tests on that build.
# This is the check CI runs. See docs/development.md.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build .\winshell.slnx -warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\winshell.slnx --no-build --nologo
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
