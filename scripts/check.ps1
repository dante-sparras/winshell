#requires -Version 7.0
# Builds, formats, and tests by default. -Lint checks Markdown and workflows without building.
[CmdletBinding(DefaultParameterSetName = 'Tests')]
param(
    [Parameter(ParameterSetName = 'Tests')]
    [switch] $NoBuild,
    [Parameter(ParameterSetName = 'Tests')]
    [ValidateSet('All', 'Fast', 'Slow')]
    [string] $Tier = 'Fast',
    [Parameter(ParameterSetName = 'Tests')]
    [int] $Seed = 0,
    [Parameter(Mandatory, ParameterSetName = 'Lint')]
    [switch] $Lint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot

if ($Lint) {
    foreach ($tool in @('lychee', 'actionlint', 'zizmor')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is required for -Lint. Install the pinned tools listed in docs/development.md#local-lint-setup."
        }
    }
    Push-Location $root
    try {
        lychee --offline --include-fragments --no-progress '**/*.md' '.github/**/*.md'
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        actionlint
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        zizmor --offline --no-progress --format plain .github
        exit $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

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
        # ReSharper checks an isolated copy; compiler warnings and analyzers remain build checks.
        & (Join-Path $PSScriptRoot 'resharper.ps1') -Mode Check
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
