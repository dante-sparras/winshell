#requires -Version 7.0
# The main test stage of check.ps1. A test that fails, fails the run: nothing retries it, and a test that fails sometimes
# is a bug to fix. Test assemblies run side by side, in an order chosen by -Seed, and the run prints that seed. Pass it
# back to run the same order again.
# -Tier Fast leaves out the tests that start winshell.exe or open a real window, which are Category=Slow, and -Tier Slow
# runs only those. ci runs them as two jobs.
param(
    [ValidateSet('All', 'Fast', 'Slow')]
    [string] $Tier = 'All',
    [int] $Seed = 0
)

$script:Root = Split-Path -Parent $PSScriptRoot
$script:TrxNamespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
$script:MainFilter = 'Category!=Package'

function Get-MainTestFilter {
    param([string] $Tier = 'All')
    switch ($Tier) {
        'Fast' { "$script:MainFilter&Category!=Slow" }
        'Slow' { "$script:MainFilter&Category=Slow" }
        default { $script:MainFilter }
    }
}

# The tests in a results folder. Complete is false unless every test project wrote a file and none of them crashed.
function Read-TestResult {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)][int] $ProjectCount
    )
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -ErrorAction SilentlyContinue)
    $failed = [Collections.Generic.HashSet[string]]::new()
    $passed = [Collections.Generic.HashSet[string]]::new()
    $complete = $files.Count -eq $ProjectCount
    $count = 0
    foreach ($file in $files) {
        $xml = [xml]::new()
        $xml.Load($file.FullName)
        $names = [Xml.XmlNamespaceManager]::new($xml.NameTable)
        $names.AddNamespace('t', $script:TrxNamespace)
        # An ordinary failure also leaves an Error here, xUnit's [FAIL] line, so the kind of error isn't enough.
        if ($xml.SelectNodes('//t:RunInfo', $names) | Where-Object { $_.InnerText -match 'test run was aborted' }) { $complete = $false }
        $summary = $xml.SelectSingleNode('//t:ResultSummary', $names).GetAttribute('outcome')
        $failures = [int] $xml.SelectSingleNode('//t:Counters', $names).GetAttribute('failed')
        if ($summary -ne 'Completed' -and $failures -eq 0) { $complete = $false }
        $tests = @{}
        foreach ($test in $xml.SelectNodes('//t:UnitTest', $names)) {
            $method = $test.SelectSingleNode('t:TestMethod', $names)
            $tests[$test.GetAttribute('id')] = $method.GetAttribute('className') + '.' + $method.GetAttribute('name')
        }
        foreach ($result in $xml.SelectNodes('//t:UnitTestResult', $names)) {
            $count++
            $name = $tests[$result.GetAttribute('testId')]
            switch ($result.GetAttribute('outcome')) {
                'Passed' { [void] $passed.Add($name) }
                'Failed' { [void] $failed.Add($name) }
            }
        }
    }
    # A theory has one result per row, so a name can be in both sets.
    [pscustomobject]@{ Failed = @($failed); Passed = @($passed | Where-Object { -not $failed.Contains($_) }); Results = $count; Complete = $complete }
}

function Invoke-DotnetTest {
    param(
        [Parameter(Mandatory)][string] $Filter,
        [Parameter(Mandatory)][string] $ResultsDirectory,
        [string] $Target = (Join-Path $script:Root 'winshell.slnx'),
        [int] $Seed = 1
    )
    # The hang timeout ends the run and names the test, which the ci step's own time limit can't do.
    # More threads than cores, so tests that need the same thing to themselves meet here and not on a slower machine.
    dotnet test $Target --no-build --nologo --filter $Filter --logger trx --results-directory $ResultsDirectory --blame-hang-timeout 3m --blame-hang-dump-type none -- "xUnit.Seed=$Seed" xUnit.MaxParallelThreads=2x | Out-Host
    $LASTEXITCODE
}

function Invoke-MainTest {
    param(
        [string] $Tier = 'All',
        [Parameter(Mandatory)][int] $ProjectCount,
        [int] $Seed = 0
    )
    if ($Seed -eq 0) { $Seed = Get-Random -Minimum 1 -Maximum ([int]::MaxValue) }
    Write-Host "Test order seed: $Seed. To run this order again: pwsh scripts/run-tests.ps1 -Tier $Tier -Seed $Seed"
    $results = Join-Path ([IO.Path]::GetTempPath()) ('winshell-test-results-' + [guid]::NewGuid().ToString('N'))
    try {
        $exit = Invoke-DotnetTest -Filter (Get-MainTestFilter -Tier $Tier) -ResultsDirectory $results -Seed $Seed
        if ($exit -ne 0) { return $exit }
        # dotnet test exits 0 for a filter it can't read, having run nothing, and for a project it left out.
        $outcome = Read-TestResult -Directory $results -ProjectCount $ProjectCount
        if ($outcome.Passed.Count -gt 0 -and $outcome.Complete) { return 0 }
        Write-Host 'No test ran, or a test project wrote no results.'
        return 1
    }
    finally {
        Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $count = @(Get-ChildItem -Path (Join-Path $script:Root 'tests\*\*.Tests.csproj')).Count
    exit (Invoke-MainTest -Tier $Tier -ProjectCount $count -Seed $Seed)
}
