# Builds winshell.slnx with warnings as errors, and the docs site, where a broken link or anchor between pages is an
# error. Then runs the tests on that build.
# The reload leak test counts process-wide handles and USER objects, so it runs in its own process.
# The QML parser corpus parses every prefix of the shipped files, so it runs alone. Next to the UI tests it
# starves them.
# The performance budgets run last and alone, so no other test competes with them for the CPU.
# This is the check CI runs. When Pester 5.6.1 is installed it also runs the three Pester suites.
# See docs/development.md.
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
    dotnet test .\winshell.slnx --no-build --nologo --filter 'Category!=Performance&Category!=Leak&Category!=Corpus'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\winshell.slnx --no-build --nologo --filter 'Category=Leak'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\tests\Winshell.Qml.Tests --no-build --nologo --filter 'Category=Corpus'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\tests\Winshell.Runtime.Tests --no-build --nologo --filter 'Category=Performance' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $pester = @(Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -eq [version]'5.6.1' })
    if ($pester.Count -eq 0) {
        Write-Host 'Pester 5.6.1 is not installed, so Install.Tests.ps1, Uninstall.Tests.ps1 and PublishRelease.Tests.ps1 were skipped.'
        exit 0
    }
    Import-Module Pester -RequiredVersion 5.6.1
    # -CI would write testResults.xml into the checkout, so the result is checked here instead.
    $result = Invoke-Pester -Path 'scripts\Install.Tests.ps1', 'scripts\Uninstall.Tests.ps1', 'scripts\PublishRelease.Tests.ps1' -PassThru
    if ($result.Result -ne 'Passed') { exit 1 }
}
finally {
    Pop-Location
}
