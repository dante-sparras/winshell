# Private test runner for check.ps1. Exit codes alone do not prove every project ran tests.
$script:Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:TrxNamespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'

function Get-MainTestFilter {
    param([string] $Tier = 'All')
    switch ($Tier) {
        'Fast' { 'Category!=Package&Category!=Slow' }
        'Slow' { 'Category!=Package&Category=Slow' }
        default { 'Category!=Package' }
    }
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
        [Parameter(Mandatory)][string] $Filter,
        [Parameter(Mandatory)][string] $ResultsDirectory,
        [int] $Seed = 1
    )
    # A native hang timeout ends the run and names the test. More threads than cores expose contention.
    dotnet test (Join-Path $script:Root 'winshell.slnx') --no-build --nologo --filter $Filter --logger trx --results-directory $ResultsDirectory --blame-hang-timeout 3m --blame-hang-dump-type none -- "xUnit.Seed=$Seed" xUnit.MaxParallelThreads=2x | Out-Host
    $LASTEXITCODE
}

function Invoke-MainTest {
    param(
        [string] $Tier = 'Fast',
        [Parameter(Mandatory)][int] $ProjectCount,
        [int] $Seed = 0
    )
    if ($Seed -eq 0) { $Seed = Get-Random -Minimum 1 -Maximum ([int]::MaxValue) }
    Write-Host "Test order seed: $Seed. To run this order again: pwsh scripts/check.ps1 -Tier $Tier -Seed $Seed"
    $results = Join-Path ([IO.Path]::GetTempPath()) ('winshell-test-results-' + [guid]::NewGuid().ToString('N'))
    try {
        $exit = Invoke-DotnetTest -Filter (Get-MainTestFilter -Tier $Tier) -ResultsDirectory $results -Seed $Seed
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
