# Builds winshell.slnx with warnings as errors, then runs the tests on that build.
# The performance budgets run last and alone, so no other test competes with them for the CPU.
# This is the check CI runs. See docs/development.md.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build .\winshell.slnx -warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\winshell.slnx --no-build --nologo --filter 'Category!=Performance'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\tests\Winshell.Runtime.Tests --no-build --nologo --filter 'Category=Performance' --logger 'console;verbosity=detailed'
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
