# Builds winshell.slnx with warnings as errors, checks its formatting, and builds the docs site, where a broken link or
# anchor between pages is an error. Then runs the tests on that build.
# run-tests.ps1 runs the main stage.
# A push to main runs all of this in the ci job, and a pull request runs -Quick, both with -Tier Fast: the slow job runs
# the rest. When Pester 5.6.1 is installed the full check also runs the Pester suites.
# See docs/development.md.
param(
    # The build, the formatting and the main test stage: what a pull request waits for.
    [switch] $Quick,
    # Skips the build, for a caller that has just built.
    [switch] $NoBuild,
    # Which tests the main stage runs. See run-tests.ps1.
    [ValidateSet('All', 'Fast', 'Slow')]
    [string] $Tier = 'All'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $NoBuild) {
        dotnet build .\winshell.slnx -warnaserror --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    # Whitespace alone, in a quarter of the time: the build already fails on style and analyzers.
    dotnet format whitespace .\winshell.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not $Quick) {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet docfx .\docs\docfx.json --warningsAsErrors --logLevel warning
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    & (Join-Path $PSScriptRoot 'run-tests.ps1') -Tier $Tier
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Quick) { exit 0 }
    $pester = @(Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -eq [version]'5.6.1' })
    if ($pester.Count -eq 0) {
        Write-Host 'Pester 5.6.1 is not installed, so Install.Tests.ps1, Uninstall.Tests.ps1, PublishRelease.Tests.ps1, RunTests.Tests.ps1, PrRules.Tests.ps1 and ReleaseGate.Tests.ps1 were skipped.'
        exit 0
    }
    Import-Module Pester -RequiredVersion 5.6.1
    # -CI would write testResults.xml into the checkout, so the result is checked here instead.
    $result = Invoke-Pester -Path 'scripts\Install.Tests.ps1', 'scripts\Uninstall.Tests.ps1', 'scripts\PublishRelease.Tests.ps1', 'scripts\RunTests.Tests.ps1', 'scripts\PrRules.Tests.ps1', 'scripts\ReleaseGate.Tests.ps1' -PassThru
    if ($result.Result -ne 'Passed') { exit 1 }
}
finally {
    Pop-Location
}
