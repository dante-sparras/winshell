# Builds winshell.slnx with warnings as errors, and the docs site, where a broken link or anchor between pages is an
# error. Then runs the tests on that build.
# The reload leak test counts process-wide handles and USER objects, so it runs in its own process.
# The QML parser corpus parses every prefix of the shipped files, in Release on every core, so it runs alone. Next to
# the UI tests it starves them.
# The performance budgets run last and alone, so no other test competes with them for the CPU.
# The media session test needs Windows to publish a session, which CI runners often don't, so the media job in ci.yml
# runs it instead.
# run-tests.ps1 runs the main stage: it leaves out tests/quarantine.txt and reruns a failed test once.
# A push to main runs all of this in the ci job. A pull request runs -Quick. When Pester 5.6.1 is installed the full
# check also runs the Pester suites.
# See docs/development.md.
param(
    # The build and the main test stage: what a pull request waits for.
    [switch] $Quick
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build .\winshell.slnx -warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not $Quick) {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet docfx .\docs\docfx.json --warningsAsErrors --logLevel warning
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    & (Join-Path $PSScriptRoot 'run-tests.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Quick) { exit 0 }
    dotnet test .\winshell.slnx --no-build --nologo --filter 'Category=Leak'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\tests\Winshell.Qml.Tests -c Release --nologo --filter 'Category=Corpus'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\tests\Winshell.Runtime.Tests --no-build --nologo --filter 'Category=Performance' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $pester = @(Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -eq [version]'5.6.1' })
    if ($pester.Count -eq 0) {
        Write-Host 'Pester 5.6.1 is not installed, so Install.Tests.ps1, Uninstall.Tests.ps1, PublishRelease.Tests.ps1, FlakyTests.Tests.ps1 and ReleaseGate.Tests.ps1 were skipped.'
        exit 0
    }
    Import-Module Pester -RequiredVersion 5.6.1
    # -CI would write testResults.xml into the checkout, so the result is checked here instead.
    $result = Invoke-Pester -Path 'scripts\Install.Tests.ps1', 'scripts\Uninstall.Tests.ps1', 'scripts\PublishRelease.Tests.ps1', 'scripts\FlakyTests.Tests.ps1', 'scripts\ReleaseGate.Tests.ps1' -PassThru
    if ($result.Result -ne 'Passed') { exit 1 }
}
finally {
    Pop-Location
}
