# Private test runner for check.ps1. Exit codes alone do not prove every project ran tests.
$script:Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:TrxNamespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'

# What a tier tests: the solution, or one test project, and a filter. Docs builds the guide samples and checks the
# generated reference; Package packs the template and runs a shell from it. Each test project the run builds writes one
# results file, so ProjectCount is how many to expect.
function Get-TestRun {
    param([string] $Tier = 'All')
    $run = switch ($Tier) {
        'Fast' { @{ Project = 'winshell.slnx'; Filter = 'Category!=Package&Category!=Slow' } }
        'Slow' { @{ Project = 'winshell.slnx'; Filter = 'Category!=Package&Category=Slow' } }
        'Docs' { @{ Project = 'tests\Winshell.Shell.Tests'; Filter = 'FullyQualifiedName~Winshell.ShellTests.DocsTests' } }
        'Package' { @{ Project = 'tests\Winshell.Package.Tests'; Filter = 'Category=Package' } }
        default { @{ Project = 'winshell.slnx'; Filter = 'Category!=Package' } }
    }
    $run.ProjectCount = 1
    if ($run.Project -like '*.slnx') {
        $solution = [xml] (Get-Content -LiteralPath (Join-Path $script:Root $run.Project) -Raw)
        $run.ProjectCount = @($solution.SelectNodes('//Project') | Where-Object { $_.Path -like '*.Tests.csproj' }).Count
    }
    $run
}

function Read-TestResult {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)][int] $ProjectCount
    )
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -ErrorAction SilentlyContinue)
    $passed = 0
    $failed = 0
    $complete = $files.Count -eq $ProjectCount
    foreach ($file in $files) {
        $xml = [xml]::new()
        $xml.Load($file.FullName)
        $names = [Xml.XmlNamespaceManager]::new($xml.NameTable)
        $names.AddNamespace('t', $script:TrxNamespace)
        if ($xml.SelectNodes('//t:RunInfo', $names) | Where-Object { $_.InnerText -match 'test run was aborted' }) { $complete = $false }
        $summary = $xml.SelectSingleNode('//t:ResultSummary', $names).GetAttribute('outcome')
        $failures = [int] $xml.SelectSingleNode('//t:Counters', $names).GetAttribute('failed')
        if ($summary -ne 'Completed' -and $failures -eq 0) { $complete = $false }
        $passed += $xml.SelectNodes('//t:UnitTestResult[@outcome="Passed"]', $names).Count
        $failed += $failures
    }
    [pscustomobject]@{ Passed = $passed; Failed = $failed; Complete = $complete }
}

function Invoke-DotnetTest {
    param(
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string] $Filter,
        [Parameter(Mandatory)][string] $ResultsDirectory,
        [int] $Seed = 1,
        [switch] $Serial
    )
    # A native hang timeout ends the run and names the test. More threads than cores expose contention.
    # -m:1 is MSBuild's node count, so a Slow run starts one VSTest project after the previous one finishes.
    # Each project still writes its own results file.
    $nodes = @()
    if ($Serial) { $nodes = @('-m:1') }
    dotnet test (Join-Path $script:Root $Project) --no-build --nologo @nodes --filter $Filter --logger trx --results-directory $ResultsDirectory --blame-hang-timeout 3m --blame-hang-dump-type none -- "xUnit.Seed=$Seed" xUnit.MaxParallelThreads=2x | Out-Host
    $LASTEXITCODE
}

function Invoke-TestRun {
    param(
        [string] $Tier = 'Fast',
        [Parameter(Mandatory)][int] $ProjectCount,
        [int] $Seed = 0
    )
    if ($Seed -eq 0) { $Seed = Get-Random -Minimum 1 -Maximum ([int]::MaxValue) }
    Write-Host "Test order seed: $Seed. To run this order again: pwsh scripts/check.ps1 -Tier $Tier -Seed $Seed"
    $results = Join-Path ([IO.Path]::GetTempPath()) ('winshell-test-results-' + [guid]::NewGuid().ToString('N'))
    try {
        $run = Get-TestRun -Tier $Tier
        $exit = Invoke-DotnetTest -Project $run.Project -Filter $run.Filter -ResultsDirectory $results -Seed $Seed -Serial:($Tier -in 'Slow', 'All')
        if ($exit -ne 0) { return $exit }
        $outcome = Read-TestResult -Directory $results -ProjectCount $ProjectCount
        if ($outcome.Passed -gt 0 -and $outcome.Failed -eq 0 -and $outcome.Complete) { return 0 }
        Write-Host 'A test failed, no test ran, or a test project wrote no results.'
        return 1
    }
    finally {
        Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue
    }
}
