# Builds winshell.slnx with warnings as errors, and the docs site, where a broken link or anchor between pages is an
# error. Then runs the tests on that build.
# The reload leak test counts process-wide handles and USER objects, so it runs in its own process.
# The performance budgets run last and alone, so no other test competes with them for the CPU.
# This is the check CI runs. See docs/development.md.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build .\winshell.slnx -warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet docfx .\docs\docfx.json --warningsAsErrors --logLevel warning
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\winshell.slnx --no-build --nologo --filter 'Category!=Performance&Category!=Leak'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\winshell.slnx --no-build --nologo --filter 'Category=Leak'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\tests\Winshell.Runtime.Tests --no-build --nologo --filter 'Category=Performance' --logger 'console;verbosity=detailed'
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
