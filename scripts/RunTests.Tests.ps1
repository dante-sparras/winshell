BeforeAll {
    . (Join-Path $PSScriptRoot 'run-tests.ps1')

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

Describe 'Choosing tests' {
    It 'leaves the package tests out, and picks a tier' {
        Get-MainTestFilter | Should -Be 'Category!=Package'
        Get-MainTestFilter -Tier Fast | Should -Be 'Category!=Package&Category!=Slow'
        Get-MainTestFilter -Tier Slow | Should -Be 'Category!=Package&Category=Slow'
    }

    It 'gives dotnet test the tier it was asked for, the seed and twice the threads' {
        $stub = Join-Path $TestDrive 'stub'
        New-Item -ItemType Directory -Path $stub | Out-Null
        Set-Content -LiteralPath (Join-Path $stub 'dotnet.ps1') -Value '$args -join " " | Add-Content -LiteralPath $env:WINSHELL_STUB_LOG; exit 0'
        $path = $env:PATH
        $env:PATH = "$stub;$path"
        $env:WINSHELL_STUB_LOG = Join-Path $TestDrive 'dotnet.log'
        try {
            pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run-tests.ps1') -Tier Slow -Seed 7 | Out-Null
        }
        finally {
            $env:PATH = $path
            $logged = Get-Content -Raw -LiteralPath $env:WINSHELL_STUB_LOG
            Remove-Item Env:WINSHELL_STUB_LOG
        }
        $logged | Should -Match 'Category=Slow'
        $logged | Should -Match 'xUnit\.Seed=7'
        $logged | Should -Match 'xUnit\.MaxParallelThreads=2x'
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
        $script:Exit = 0
        $script:Rows = @('Ns.Class.Good=Passed')
        Mock Invoke-DotnetTest {
            Write-Trx -Directory $ResultsDirectory -Rows $script:Rows
            $script:Exit
        }
    }

    It 'passes when every test passes' {
        Invoke-MainTest -ProjectCount 1 -Seed 7 | Should -Be 0
        Should -Invoke Invoke-DotnetTest -Times 1 -Exactly -ParameterFilter { $Seed -eq 7 }
    }

    It 'fails on a failed test, once, with nothing run again' {
        $script:Rows = @('Ns.Class.Good=Passed', 'Ns.Class.Bad=Failed')
        $script:Exit = 1
        Invoke-MainTest -ProjectCount 1 | Should -Be 1
        Should -Invoke Invoke-DotnetTest -Times 1 -Exactly
    }

    It 'fails a run that exits 0 having run no tests' {
        $script:Rows = @()
        Invoke-MainTest -ProjectCount 1 | Should -Be 1
    }

    It 'fails a run that exits 0 with a test project missing' {
        Invoke-MainTest -ProjectCount 2 | Should -Be 1
    }

    It 'chooses a seed of its own and prints it' {
        Mock Write-Host { }
        Invoke-MainTest -ProjectCount 1 | Should -Be 0
        Should -Invoke Write-Host -ParameterFilter { $Object -match 'Test order seed: \d+' }
    }

    It 'prints how to play back the tier it ran' {
        Mock Write-Host { }
        Invoke-MainTest -Tier Fast -ProjectCount 1 -Seed 7 | Should -Be 0
        Should -Invoke Write-Host -ParameterFilter { $Object -match 'run-tests\.ps1 -Tier Fast -Seed 7' }
    }
}

Describe 'Watching a test program start' {
    BeforeAll {
        # Programs that sit as xUnit starts them, "<program> @@ <file>", with a line to each argument in the file.
        $program = (Get-Process -Id $PID).Path
        $script:Folder = Split-Path $program
        Set-Content -LiteralPath "$TestDrive\wait.ps1" -Value 'Start-Sleep -Seconds 300'
        Set-Content -LiteralPath "$TestDrive\info.txt" -Value '-assemblyInfo'
        Set-Content -LiteralPath "$TestDrive\list.txt" -Value '-automated', '-list', 'Discovery'
        $script:Info = Start-Process $program -ArgumentList '-NoProfile', '-File', "`"$TestDrive\wait.ps1`"", '@@', "`"$TestDrive\info.txt`"" -WindowStyle Hidden -PassThru
        $script:List = Start-Process $program -ArgumentList '-NoProfile', '-File', "`"$TestDrive\wait.ps1`"", '@@', "`"$TestDrive\list.txt`"" -WindowStyle Hidden -PassThru
    }

    AfterAll {
        Stop-Process -Id $script:Info.Id, $script:List.Id -Force -ErrorAction SilentlyContinue
    }

    It 'finds a program asked only for its assembly info, under the checkout' {
        $found = @(Get-StalledStart -Root $script:Folder -After 0 | ForEach-Object ProcessId)
        $found | Should -Contain $script:Info.Id
        $found | Should -Not -Contain $script:List.Id
        @(Get-StalledStart -Root (Join-Path $TestDrive 'elsewhere') -After 0) | Should -HaveCount 0
    }

    It 'leaves a program alone until it has run for a while' {
        @(Get-StalledStart -Root $script:Folder -After 3600) | Should -HaveCount 0
    }

    It 'says what the program is doing and how busy the machine is' {
        $process = Get-StalledStart -Root $script:Folder -After 0 | Where-Object ProcessId -EQ $script:Info.Id
        $text = (Format-StalledStart -Process $process) -join "`n"
        $text | Should -Match "$($script:Info.Id) was asked for its assembly info \d+ s ago and has not exited"
        $text | Should -Match 'It used [\d.]+ s of CPU\. Threads: \d+ '
        $text | Should -Match 'the runtime (started|did not start), and the Windows App SDK (loaded|did not load)'
        $text | Should -Match '\d+% CPU busy, \d+ MB of memory free, disks \d+% idle'
    }
}
