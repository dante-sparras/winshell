BeforeAll {
    . (Join-Path $PSScriptRoot '../../scripts/lib/Tests.ps1')

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
}

Describe 'Reading test results' {
    It 'counts passed and failed theory rows' {
        $directory = Join-Path $TestDrive 'theory'
        Write-Trx -Directory $directory -Rows @('Ns.Class.Theory=Passed', 'Ns.Class.Theory=Failed', 'Ns.Class.Fact=Passed')
        $result = Read-TestResult -Directory $directory -ProjectCount 1
        $result.Failed | Should -Be 1
        $result.Passed | Should -Be 2
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

    It 'fails when a results file reports failure despite exit code 0' {
        $script:Rows = @('Ns.Class.Good=Passed', 'Ns.Class.Bad=Failed')
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
        Should -Invoke Write-Host -ParameterFilter { $Object -match 'check\.ps1 -Tier Fast -Seed 7' }
    }
}

Describe 'Check command' {
    BeforeAll {
        $script:Check = (Resolve-Path (Join-Path $PSScriptRoot '../../scripts/check.ps1')).Path
        $script:ProjectCount = @(Get-ChildItem (Join-Path $PSScriptRoot '../*/*.Tests.csproj')).Count
    }

    BeforeEach {
        $script:EnvironmentBefore = @{}
        foreach ($name in @('PATH', 'PSModulePath', 'WINSHELL_CHECK_LOG', 'WINSHELL_CHECK_RESULTS', 'WINSHELL_CHECK_FAIL_PESTER')) {
            $script:EnvironmentBefore[$name] = [Environment]::GetEnvironmentVariable($name)
        }
        $script:Stub = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $modules = Join-Path $script:Stub 'modules'
        $pester = Join-Path $modules 'Pester/5.6.1'
        New-Item -ItemType Directory -Path $pester -Force | Out-Null
        $env:WINSHELL_CHECK_LOG = Join-Path $script:Stub 'calls.txt'
        $env:WINSHELL_CHECK_RESULTS = Join-Path $script:Stub 'results'
        $env:WINSHELL_CHECK_FAIL_PESTER = $null
        foreach ($i in 1..$script:ProjectCount) {
            Write-Trx -Directory $env:WINSHELL_CHECK_RESULTS -Rows @('Ns.Class.Fact=Passed')
        }
        Set-Content -LiteralPath (Join-Path $script:Stub 'dotnet.ps1') -Value @'
"dotnet $($args -join ' ')" | Add-Content -LiteralPath $env:WINSHELL_CHECK_LOG
if ($args[0] -eq 'test') {
    $dest = $args[[array]::IndexOf($args, '--results-directory') + 1]
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $env:WINSHELL_CHECK_RESULTS '*.trx') -Destination $dest
}
exit 0
'@
        Set-Content -LiteralPath (Join-Path $pester 'Pester.psm1') -Value @'
function Invoke-Pester {
    param([string[]] $Path, [switch] $PassThru)
    "Pester $Path" | Add-Content -LiteralPath $env:WINSHELL_CHECK_LOG
    [pscustomobject]@{ Result = $(if ($env:WINSHELL_CHECK_FAIL_PESTER) { 'Failed' } else { 'Passed' }) }
}
'@
        New-ModuleManifest -Path (Join-Path $pester 'Pester.psd1') -RootModule Pester.psm1 -ModuleVersion 5.6.1 -FunctionsToExport Invoke-Pester
        $env:PATH = "$script:Stub;$env:PATH"
        $env:PSModulePath = "$modules;$(Join-Path $PSHOME 'Modules')"
    }

    AfterEach {
        foreach ($name in $script:EnvironmentBefore.Keys) {
            [Environment]::SetEnvironmentVariable($name, $script:EnvironmentBefore[$name])
        }
    }

    It 'builds, formats, and runs fast .NET and tooling tests by default' {
        pwsh -NoProfile -File $script:Check | Out-Null
        $LASTEXITCODE | Should -Be 0
        $calls = Get-Content -LiteralPath $env:WINSHELL_CHECK_LOG
        $calls | Should -HaveCount 4
        $calls[0] | Should -Match '^dotnet build '
        $calls[1] | Should -Match '^dotnet format whitespace '
        $calls[2] | Should -Match '--filter Category!=Package&Category!=Slow '
        $calls[3] | Should -Be 'Pester tests/Scripts'
    }

    It 'runs only slow tests on an existing build and forwards the seed and hang timeout' {
        pwsh -NoProfile -File $script:Check -Tier Slow -NoBuild -Seed 7 | Out-Null
        $LASTEXITCODE | Should -Be 0
        $calls = @(Get-Content -LiteralPath $env:WINSHELL_CHECK_LOG)
        $calls | Should -HaveCount 1
        $calls[0] | Should -Match '--filter Category!=Package&Category=Slow '
        $calls[0] | Should -Match 'xUnit.Seed=7'
        $calls[0] | Should -Match 'xUnit.MaxParallelThreads=2x'
        $calls[0] | Should -Match '--blame-hang-timeout 3m'
    }

    It 'fails before building when the pinned Pester version is missing' {
        Remove-Item -LiteralPath (Join-Path $script:Stub 'modules/Pester/5.6.1/Pester.psd1')
        $output = pwsh -NoProfile -File $script:Check 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($output -join "`n") | Should -Match 'Pester 5.6.1 is required'
        Test-Path -LiteralPath $env:WINSHELL_CHECK_LOG | Should -BeFalse
    }

    It 'fails when the tooling suite fails' {
        $env:WINSHELL_CHECK_FAIL_PESTER = '1'
        pwsh -NoProfile -File $script:Check | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}
