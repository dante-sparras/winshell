BeforeAll {
    . (Join-Path $PSScriptRoot 'run-tests.ps1')
    . (Join-Path $PSScriptRoot 'report-flaky.ps1')

    # Writes a results file like dotnet test does. Rows are "Class.Method=Outcome".
    function Write-Trx {
        param([string] $Directory, [string[]] $Rows, [switch] $Crashed, [switch] $NoAbortText)
        New-Item -ItemType Directory -Force -Path $Directory | Out-Null
        $definitions = ''
        $results = ''
        foreach ($row in $Rows) {
            $name, $outcome = $row -split '='
            $id = [guid]::NewGuid()
            $class = $name.Substring(0, $name.LastIndexOf('.'))
            $method = $name.Substring($name.LastIndexOf('.') + 1)
            $definitions += "<UnitTest name=`"$name(a: 1)`" id=`"$id`"><TestMethod className=`"$class`" name=`"$method`" /></UnitTest>"
            $results += "<UnitTestResult testId=`"$id`" testName=`"$name`" outcome=`"$outcome`" />"
        }
        $failures = @($Rows | Where-Object { $_ -like '*=Failed' }).Count
        $outcome = if ($Crashed -or $NoAbortText -or $failures -gt 0) { 'Failed' } else { 'Completed' }
        $info = if ($Crashed) {
            '<RunInfo outcome="Error"><Text>The active test run was aborted. Reason: Test host process crashed</Text></RunInfo>'
        }
        elseif ($failures -gt 0) {
            '<RunInfo outcome="Error"><Text>[xUnit.net 00:00:00.21] Ns.Class.Test [FAIL]</Text></RunInfo>'
        }
        else {
            '<RunInfo outcome="Warning"><Text>No test matches</Text></RunInfo>'
        }
        $xml = "<TestRun xmlns=`"$script:TrxNamespace`"><Results>$results</Results><TestDefinitions>$definitions</TestDefinitions><ResultSummary outcome=`"$outcome`"><Counters failed=`"$failures`" /><RunInfos>$info</RunInfos></ResultSummary></TestRun>"
        Set-Content -LiteralPath (Join-Path $Directory ([guid]::NewGuid().ToString('N') + '.trx')) -Value $xml
    }
}

Describe 'The quarantine list' {
    It 'skips comments and blank lines' {
        $path = Join-Path $TestDrive 'quarantine.txt'
        Set-Content -LiteralPath $path -Value @('# a note', '', '  Ns.Class.One  ', 'Ns.Class.Two')
        Get-QuarantinedTest -Path $path | Should -Be @('Ns.Class.One', 'Ns.Class.Two')
    }

    It 'is left out of the main filter and is all the quarantine filter selects' {
        $filter = Get-MainTestFilter -Quarantined @('Ns.Class.One', 'Ns.Class.Two')
        $filter | Should -BeLike 'Category!=Performance&*&Category!=Media&FullyQualifiedName!=Ns.Class.One&FullyQualifiedName!=Ns.Class.Two'
        Get-NamedTestFilter -Names @('Ns.Class.One', 'Ns.Class.Two') | Should -Be 'FullyQualifiedName=Ns.Class.One|FullyQualifiedName=Ns.Class.Two'
    }

    It 'leaves the main filter alone when nothing is quarantined' {
        Get-MainTestFilter | Should -Be 'Category!=Performance&Category!=Leak&Category!=Corpus&Category!=Media'
    }

    It 'adds no clause for a list with only comments' {
        $path = Join-Path $TestDrive 'comments.txt'
        Set-Content -LiteralPath $path -Value @('# only a note')
        $list = Get-QuarantinedTest -Path $path
        Get-MainTestFilter -Quarantined $list | Should -Be 'Category!=Performance&Category!=Leak&Category!=Corpus&Category!=Media'
        Get-MainTestFilter -Quarantined @($list) | Should -Be 'Category!=Performance&Category!=Leak&Category!=Corpus&Category!=Media'
    }

    It 'runs nothing when nothing is quarantined' {
        Mock dotnet { throw 'dotnet should not run' }
        Invoke-QuarantinedTest | Should -Be 0
        Should -Invoke dotnet -Times 0
    }

    It 'passes on the failure of a quarantined test' {
        Mock dotnet { $global:LASTEXITCODE = 1 }
        Invoke-QuarantinedTest -Quarantined @('Ns.Class.One') | Should -Be 1
    }
}

Describe 'Reading test results' {
    It 'names a failed theory once and does not count it as passed' {
        $directory = Join-Path $TestDrive 'theory'
        Write-Trx -Directory $directory -Rows @('Ns.Class.Theory=Passed', 'Ns.Class.Theory=Failed', 'Ns.Class.Fact=Passed')
        $result = Read-TestResult -Directory $directory -ProjectCount 1
        $result.Failed | Should -Be @('Ns.Class.Theory')
        $result.Passed | Should -Be @('Ns.Class.Fact')
        $result.Complete | Should -BeTrue
    }

    It 'is not complete when a test host crashed' {
        $directory = Join-Path $TestDrive 'crash'
        Write-Trx -Directory $directory -Rows @('Ns.Class.Fact=Failed') -Crashed
        (Read-TestResult -Directory $directory -ProjectCount 1).Complete | Should -BeFalse
    }

    It 'is complete after an ordinary failure' {
        $directory = Join-Path $TestDrive 'failure'
        Write-Trx -Directory $directory -Rows @('Ns.Class.Fact=Failed')
        (Read-TestResult -Directory $directory -ProjectCount 1).Complete | Should -BeTrue
    }

    It 'is not complete when a run failed with no failed test' {
        $directory = Join-Path $TestDrive 'silent'
        Write-Trx -Directory $directory -Rows @('Ns.Class.Fact=Passed') -NoAbortText
        (Read-TestResult -Directory $directory -ProjectCount 1).Complete | Should -BeFalse
    }

    It 'is not complete when a test project wrote no results' {
        $directory = Join-Path $TestDrive 'missing'
        Write-Trx -Directory $directory -Rows @('Ns.Class.Fact=Failed')
        (Read-TestResult -Directory $directory -ProjectCount 2).Complete | Should -BeFalse
    }
}

Describe 'Running the main stage' {
    BeforeEach {
        $script:Report = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.txt')
        $script:Runs = [Collections.Generic.List[object]]::new()
        # One entry per dotnet test run: the exit code, the rows it reports, and whether it crashed.
        $script:Plan = @()
        Mock Invoke-DotnetTest {
            $step = $script:Plan[$script:Runs.Count]
            $script:Runs.Add($Filter)
            Write-Trx -Directory $ResultsDirectory -Rows $step.Rows -Crashed:([bool] $step['Crashed'])
            return $step.Exit
        }
    }

    It 'passes without a rerun when every test passes' {
        $script:Plan = @(@{ Exit = 0; Rows = @('Ns.Class.One=Passed') })
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 0
        $script:Runs.Count | Should -Be 1
        Test-Path -LiteralPath $script:Report | Should -BeFalse
    }

    It 'fails a run that exits 0 having run no tests' {
        $script:Plan = @(@{ Exit = 0; Rows = @() })
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
    }

    It 'fails a run that exits 0 with a test project missing' {
        $script:Plan = @(@{ Exit = 0; Rows = @('Ns.Class.One=Passed') })
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 2 | Should -Be 1
    }

    It 'passes and reports a test that passes on its rerun' {
        $script:Plan = @(
            @{ Exit = 1; Rows = @('Ns.Class.One=Failed', 'Ns.Class.Two=Passed') }
            @{ Exit = 0; Rows = @('Ns.Class.One=Passed') }
        )
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 0
        $script:Runs[1] | Should -Be 'FullyQualifiedName=Ns.Class.One'
        Get-Content -LiteralPath $script:Report | Should -Be @('Ns.Class.One')
    }

    It 'fails a test that fails twice' {
        $script:Plan = @(
            @{ Exit = 1; Rows = @('Ns.Class.One=Failed') }
            @{ Exit = 1; Rows = @('Ns.Class.One=Failed') }
        )
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
        Test-Path -LiteralPath $script:Report | Should -BeFalse
    }

    It 'fails when only some failed tests pass on the rerun, and reports none' {
        $script:Plan = @(
            @{ Exit = 1; Rows = @('Ns.Class.One=Failed', 'Ns.Class.Two=Failed') }
            @{ Exit = 1; Rows = @('Ns.Class.One=Passed', 'Ns.Class.Two=Failed') }
        )
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
        Test-Path -LiteralPath $script:Report | Should -BeFalse
    }

    It 'fails when the rerun exits 0 without running the test' {
        $script:Plan = @(
            @{ Exit = 1; Rows = @('Ns.Class.One=Failed') }
            @{ Exit = 0; Rows = @() }
        )
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
    }

    It 'does not rerun a run that crashed' {
        $script:Plan = @(@{ Exit = 1; Rows = @('Ns.Class.One=Failed'); Crashed = $true })
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
        $script:Runs.Count | Should -Be 1
    }

    It 'does not rerun a failed run with no failed tests' {
        $script:Plan = @(@{ Exit = 1; Rows = @('Ns.Class.One=Passed') })
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
        $script:Runs.Count | Should -Be 1
    }

    It 'does not rerun more failures than a flake explains' {
        $rows = @(1..11 | ForEach-Object { "Ns.Class.Test$_=Failed" })
        $script:Plan = @(@{ Exit = 1; Rows = $rows })
        Invoke-MainTest -ReportPath $script:Report -ProjectCount 1 | Should -Be 1
        $script:Runs.Count | Should -Be 1
    }

    It 'leaves quarantined tests out of the first run' {
        $script:Plan = @(@{ Exit = 0; Rows = @('Ns.Class.One=Passed') })
        Invoke-MainTest -Quarantined @('Ns.Class.Held') -ReportPath $script:Report -ProjectCount 1 | Should -Be 0
        $script:Runs[0] | Should -BeLike '*&FullyQualifiedName!=Ns.Class.Held'
    }
}

Describe 'Reporting a flaky test' {
    BeforeEach {
        $script:Report = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.txt')
        Set-Content -LiteralPath $script:Report -Value @('Ns.Class.One')
        $script:Calls = [Collections.Generic.List[string]]::new()
        $script:Open = @()
        $script:FailOn = ''
        $global:LASTEXITCODE = 0
        Mock gh {
            $script:Calls.Add(($args -join ' '))
            $global:LASTEXITCODE = if ($script:FailOn -and $args[1] -eq $script:FailOn) { 1 } else { 0 }
            if ($args[1] -eq 'list') { return (ConvertTo-Json -InputObject @($script:Open)) }
        }
    }

    It 'files an issue labeled flaky when none is open' {
        Publish-FlakyReport -ReportPath $script:Report -RunUrl 'https://run'
        $script:Calls[1] | Should -BeLike 'issue create --title Flaky test: Ns.Class.One --label flaky --body *https://run*'
    }

    It 'comments on the open issue of that test' {
        $script:Open = @(@{ number = 12; title = 'Flaky test: Ns.Class.One' })
        Publish-FlakyReport -ReportPath $script:Report -RunUrl 'https://run'
        $script:Calls[1] | Should -BeLike 'issue comment 12 --body *https://run'
        $script:Calls.Count | Should -Be 2
    }

    It 'does not comment on the issue of a test with a longer name' {
        $script:Open = @(@{ number = 12; title = 'Flaky test: Ns.Class.One_more' })
        Publish-FlakyReport -ReportPath $script:Report -RunUrl 'https://run'
        $script:Calls[1] | Should -BeLike 'issue create *'
    }

    It 'throws when an issue cannot be filed' {
        $script:FailOn = 'create'
        { Publish-FlakyReport -ReportPath $script:Report -RunUrl 'https://run' } | Should -Throw '*Ns.Class.One*'
    }
}
