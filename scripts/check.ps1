#requires -Version 7.0
# Builds, checks formatting, and runs fast .NET and PowerShell tests. Slow tests require -Tier Slow or All.
param(
    [switch] $NoBuild,
    [ValidateSet('All', 'Fast', 'Slow')]
    [string] $Tier = 'Fast',
    [int] $Seed = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'lib/Tests.ps1')

# Check prerequisites before spending time on a build. The slow job runs only .NET tests.
if ($Tier -ne 'Slow') {
    if (-not (Get-Module -ListAvailable -Name Pester | Where-Object Version -EQ ([version]'5.6.1'))) {
        throw 'Pester 5.6.1 is required. Run: Install-Module Pester -RequiredVersion 5.6.1 -Scope CurrentUser -Force -SkipPublisherCheck'
    }
    Import-Module Pester -RequiredVersion 5.6.1
}

Push-Location $root
try {
    if (-not $NoBuild) {
        dotnet build .\winshell.slnx -warnaserror --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if ($Tier -ne 'Slow') {
        # The build already checks style and analyzers.
        dotnet format whitespace .\winshell.slnx --verify-no-changes --no-restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $count = @(Get-ChildItem -Path (Join-Path $root 'tests/*/*.Tests.csproj')).Count
    $exit = Invoke-MainTest -Tier $Tier -ProjectCount $count -Seed $Seed
    if ($exit -ne 0) { exit $exit }
    if ($Tier -ne 'Slow') {
        # -CI writes testResults.xml into the checkout; inspect the result instead.
        $result = & {
            # Pester suites control their own strict mode, independently of this command.
            Set-StrictMode -Off
            Invoke-Pester -Path 'tests/Scripts' -PassThru
        }
        if ($result.Result -ne 'Passed') { exit 1 }
    }
}
finally {
    Pop-Location
}
exit 0
