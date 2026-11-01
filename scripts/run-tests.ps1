#requires -Version 7.0
# The main test stage of check.ps1. Tests listed in tests/quarantine.txt are left out.
# A failed test runs once more. If it passes then, it is flaky: it is written to the report and the run passes. If it
# fails again, the run fails. A run that did not finish, or more failures than a flake explains, is not rerun.
# With -Quarantined, runs only the listed tests, once. See "Flaky tests" in CONTRIBUTING.md.
param(
    [switch] $Quarantined,
    [string] $ReportPath = $env:WINSHELL_FLAKY_REPORT
)

$script:Root = Split-Path -Parent $PSScriptRoot
$script:TrxNamespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
$script:MainFilter = 'Category!=Performance&Category!=Leak&Category!=Corpus&Category!=Media'
$script:MaxRerun = 10

function Get-QuarantinedTest {
    param([Parameter(Mandatory)][string] $Path)
    @(Get-Content -LiteralPath $Path | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}

function Get-MainTestFilter {
    param([string[]] $Quarantined = @())
    (@($script:MainFilter) + @($Quarantined | Where-Object { $_ } | ForEach-Object { "FullyQualifiedName!=$_" })) -join '&'
}

function Get-NamedTestFilter {
    param([Parameter(Mandatory)][string[]] $Names)
    @($Names | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
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
            $name = $tests[$result.GetAttribute('testId')]
            switch ($result.GetAttribute('outcome')) {
                'Passed' { [void] $passed.Add($name) }
                'Failed' { [void] $failed.Add($name) }
            }
        }
    }
    # A theory has one result per row, so a name can be in both sets.
    [pscustomobject]@{ Failed = @($failed); Passed = @($passed | Where-Object { -not $failed.Contains($_) }); Complete = $complete }
}

function Invoke-DotnetTest {
    param(
        [Parameter(Mandatory)][string] $Filter,
        [Parameter(Mandatory)][string] $ResultsDirectory
    )
    # The hang timeout ends the run and names the test, which the ci step's own time limit can't do.
    dotnet test (Join-Path $script:Root 'winshell.slnx') --no-build --nologo --filter $Filter --logger trx --results-directory $ResultsDirectory --blame-hang-timeout 3m --blame-hang-dump-type none | Out-Host
    $LASTEXITCODE
}

function Invoke-MainTest {
    param(
        [string[]] $Quarantined = @(),
        [string] $ReportPath,
        [Parameter(Mandatory)][int] $ProjectCount
    )
    $results = Join-Path ([IO.Path]::GetTempPath()) ('winshell-test-results-' + [guid]::NewGuid().ToString('N'))
    try {
        $firstRun = Join-Path $results 'first'
        $exit = Invoke-DotnetTest -Filter (Get-MainTestFilter -Quarantined $Quarantined) -ResultsDirectory $firstRun
        $first = Read-TestResult -Directory $firstRun -ProjectCount $ProjectCount
        if ($exit -eq 0) {
            # dotnet test exits 0 for a filter it can't read, having run nothing, and for a project it left out.
            if ($first.Passed.Count -gt 0 -and $first.Complete) { return 0 }
            Write-Host 'No test ran, or a test project wrote no results.'
            return 1
        }
        if (-not $first.Complete -or $first.Failed.Count -eq 0) {
            Write-Host 'The test run did not finish, so there is nothing to rerun.'
            return $exit
        }
        if ($first.Failed.Count -gt $script:MaxRerun) {
            Write-Host "$($first.Failed.Count) tests failed. That is not a flake, so they are not rerun."
            return $exit
        }
        Write-Host "Running $($first.Failed.Count) failed test(s) once more."
        $secondRun = Join-Path $results 'second'
        $exit = Invoke-DotnetTest -Filter (Get-NamedTestFilter -Names $first.Failed) -ResultsDirectory $secondRun
        if ($exit -ne 0) { return $exit }
        # A test the rerun did not run has not passed, whatever the exit code says.
        $second = Read-TestResult -Directory $secondRun -ProjectCount $ProjectCount
        $missing = @($first.Failed | Where-Object { $second.Passed -cnotcontains $_ })
        if ($missing.Count -gt 0) {
            Write-Host "The rerun did not pass: $($missing -join ', ')"
            return 1
        }
        foreach ($name in $first.Failed) { Write-Host "::warning title=Flaky test::$name failed and then passed on its rerun." }
        if ($ReportPath) { Set-Content -LiteralPath $ReportPath -Value $first.Failed }
        return 0
    }
    finally {
        Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-QuarantinedTest {
    param([string[]] $Quarantined = @())
    if ($Quarantined.Count -eq 0) {
        Write-Host 'No test is quarantined.'
        return 0
    }
    dotnet test (Join-Path $script:Root 'winshell.slnx') --nologo --filter (Get-NamedTestFilter -Names $Quarantined) | Out-Host
    $LASTEXITCODE
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $list = @(Get-QuarantinedTest -Path (Join-Path $script:Root 'tests\quarantine.txt'))
    if ($Quarantined) {
        exit (Invoke-QuarantinedTest -Quarantined $list)
    }
    $projects = @(Get-ChildItem -Path (Join-Path $script:Root 'tests\*\*.Tests.csproj')).Count
    exit (Invoke-MainTest -Quarantined $list -ReportPath $ReportPath -ProjectCount $projects)
}
