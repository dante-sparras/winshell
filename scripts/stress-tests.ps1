#requires -Version 7.0
# Runs the tests a change adds or edits many times with every core busy. One failure fails the run, because a test that
# fails once in 30 gets past a single run and the rerun in run-tests.ps1.
# Tests that ci runs alone or leaves out are not stressed: the categories run-tests.ps1 filters, and tests/quarantine.txt.
# After BudgetMinutes it stops between rounds and warns, so a bulk edit can't time out the ci job.
# A failed test goes into the file WINSHELL_FLAKY_REPORT names, when that is set.
# Needs a build; the ci job runs it after check.ps1 on a push to main. See "Flaky tests" in CONTRIBUTING.md.
param(
    # The default suits ci, where HEAD is the commit a merge put on main. Locally, use -Base origin/main.
    [string] $Base = 'HEAD^1',
    [int] $Rounds = 30,
    [int] $BudgetMinutes = 15
)

. (Join-Path $PSScriptRoot 'run-tests.ps1')

$script:IsolatedTrait = 'Trait\("Category",\s*"(?:Performance|Leak|Corpus|Media)"'

# Where each file changed, as line numbers after the change, from `git diff --unified=0`. A hunk that only deletes has
# no lines of its own, so it is the gap after the line it names: 4.5 sits between lines 4 and 5.
function Read-ChangedLine {
    param([string[]] $Diff = @())
    $changed = @{}
    $lines = $null
    foreach ($line in $Diff) {
        if ($line -match '^\+\+\+ (?:b/(?<path>.+)|/dev/null)$') {
            $lines = $null
            if ($Matches.ContainsKey('path')) {
                $lines = [Collections.Generic.List[double]]::new()
                $changed[$Matches['path']] = $lines
            }
        }
        elseif ($null -ne $lines -and $line -match '^@@ -\S+ \+(\d+)(?:,(\d+))? @@') {
            $start = [int] $Matches[1]
            $count = if ($Matches[2]) { [int] $Matches[2] } else { 1 }
            if ($count -eq 0) { $lines.Add($start + 0.5) }
            else { foreach ($number in $start..($start + $count - 1)) { $lines.Add($number) } }
        }
    }
    $changed
}

# The tests in a C# file. A test owns its attributes and its method, so a new test does not touch the one above it.
function Read-TestMethod {
    param([string[]] $Source = @())
    $tests = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $Source.Count; $i++) {
        if ($Source[$i] -notmatch '^\s*\[(?:Fact|Theory)\b') { continue }
        $first = $i
        while ($first -gt 0 -and $Source[$first - 1] -match '^\s*\[') { $first-- }
        $name = $null
        for ($j = $i + 1; $j -lt $Source.Count; $j++) {
            if ($Source[$j] -match '^(\s*)(?:public|internal)\s.*?\b(\w+)\s*\(') {
                $indent = $Matches[1]
                $name = $Matches[2]
                break
            }
        }
        if (-not $name) { throw "No test method follows line $($i + 1)." }
        # The body ends at the brace under the method, or at "});" after a lambda. With no brace it ends at its semicolon.
        $last = $Source.Count
        $block = $false
        for ($k = $j; $k -lt $Source.Count; $k++) {
            $line = $Source[$k].TrimEnd()
            if ($line -ceq "$indent{") { $block = $true }
            if ($line.StartsWith("$indent}", [StringComparison]::Ordinal) -or (-not $block -and $line.EndsWith(';'))) {
                $last = $k + 1
                break
            }
        }
        $tests.Add([pscustomobject]@{ Name = $name; First = $first + 1; Last = $last })
        $i = $last - 1
    }
    $tests
}

# The tests the commits since $Base add or edit.
function Get-ChangedTest {
    param([Parameter(Mandatory)][string] $Base)
    $diff = @(git -C $script:Root diff --unified=0 --no-color "$Base...HEAD" -- 'tests/*.cs')
    if ($LASTEXITCODE -ne 0) { throw "git diff against $Base failed." }
    $changed = Read-ChangedLine -Diff $diff
    foreach ($path in $changed.Keys) {
        $source = @(Get-Content -LiteralPath (Join-Path $script:Root $path))
        $isolated = ($source -join "`n") -match $script:IsolatedTrait
        foreach ($test in Read-TestMethod -Source $source) {
            if (@($changed[$path] | Where-Object { $_ -ge $test.First -and $_ -le $test.Last }).Count -gt 0) {
                [pscustomobject]@{ Name = $test.Name; Project = 'tests/' + ($path -split '/')[1]; Isolated = $isolated }
            }
        }
    }
}

function Start-CpuLoad {
    param([Parameter(Mandatory)][int] $Minutes)
    # Each one ends by itself, so a killed run doesn't leave a machine spinning.
    $spin = "`$end = (Get-Date).AddMinutes($Minutes); while ((Get-Date) -lt `$end) { }"
    @(1..[Environment]::ProcessorCount | ForEach-Object {
            Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $spin -PassThru -WindowStyle Hidden
        })
}

function Stop-CpuLoad {
    param([object[]] $Processes = @())
    $Processes | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Invoke-StressRound {
    param(
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string] $Filter,
        [Parameter(Mandatory)][string] $ResultsDirectory
    )
    $output = dotnet test (Join-Path $script:Root $Project) --no-build --nologo --filter $Filter --logger trx --results-directory $ResultsDirectory --blame-hang-timeout 3m --blame-hang-dump-type none 2>&1
    $exit = $LASTEXITCODE
    [pscustomobject]@{ Exit = $exit; Output = $output; Result = Read-TestResult -Directory $ResultsDirectory -ProjectCount 1 }
}

function Invoke-StressTest {
    param(
        [object[]] $Tests = @(),
        [string[]] $Quarantined = @(),
        [Parameter(Mandatory)][int] $Rounds,
        [Parameter(Mandatory)][int] $BudgetMinutes,
        [string] $ReportPath
    )
    $stressed = [Collections.Generic.List[object]]::new()
    foreach ($test in $Tests) {
        $listed = @($Quarantined | Where-Object { $_.EndsWith('.' + $test.Name, [StringComparison]::Ordinal) }).Count -gt 0
        if ($test.Isolated -or $listed) { Write-Host "::notice title=Not stressed::$($test.Name) runs alone or is quarantined." }
        else { $stressed.Add($test) }
    }
    if ($stressed.Count -eq 0) {
        Write-Host 'No test to stress.'
        return 0
    }
    $main = Get-MainTestFilter -Quarantined $Quarantined
    $projects = @($stressed | Group-Object Project)
    Write-Host "Stressing $($stressed.Count) test(s) for up to $Rounds rounds: $($stressed.Name -join ', ')"
    $results = Join-Path ([IO.Path]::GetTempPath()) ('winshell-stress-results-' + [guid]::NewGuid().ToString('N'))
    $load = Start-CpuLoad -Minutes ($BudgetMinutes + 5)
    try {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        for ($round = 1; $round -le $Rounds; $round++) {
            foreach ($project in $projects) {
                $names = @($project.Group | ForEach-Object { "FullyQualifiedName~.$($_.Name)" }) -join '|'
                $directory = Join-Path $results "$round-$($project.Name -replace '\W', '-')"
                $run = Invoke-StressRound -Project $project.Name -Filter "($main)&($names)" -ResultsDirectory $directory
                if ($run.Exit -ne 0 -or $run.Result.Failed.Count -gt 0 -or -not $run.Result.Complete) {
                    $run.Output | Out-Host
                    foreach ($name in $run.Result.Failed) { Write-Host "::error title=Flaky test::$name failed in round $round of $Rounds." }
                    # A hang or a crash names no failed test, so it reports the tests that round was running.
                    $reported = if ($run.Result.Failed.Count -gt 0) { $run.Result.Failed } else { $project.Group.Name }
                    if ($ReportPath) { Add-Content -LiteralPath $ReportPath -Value $reported }
                    Write-Host "Round $round of $Rounds failed."
                    return $(if ($run.Exit -ne 0) { $run.Exit } else { 1 })
                }
                $missing = @($project.Group | Where-Object {
                        $suffix = '.' + $_.Name
                        @($run.Result.Passed | Where-Object { $_.EndsWith($suffix, [StringComparison]::Ordinal) }).Count -eq 0
                    })
                if ($missing.Count -gt 0) {
                    Write-Host "::error title=Test not run::$($missing.Name -join ', ') did not run in round $round."
                    return 1
                }
            }
            if ($round -lt $Rounds -and $clock.Elapsed.TotalMinutes -ge $BudgetMinutes) {
                Write-Host "::warning title=Stress run cut short::Stopped after $round of $Rounds rounds, at the $BudgetMinutes minute budget."
                return 0
            }
        }
        Write-Host "All $Rounds rounds passed."
        return 0
    }
    finally {
        Stop-CpuLoad -Processes $load
        Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $list = @(Get-QuarantinedTest -Path (Join-Path $script:Root 'tests\quarantine.txt'))
    exit (Invoke-StressTest -Tests @(Get-ChangedTest -Base $Base) -Quarantined $list -Rounds $Rounds -BudgetMinutes $BudgetMinutes -ReportPath $env:WINSHELL_FLAKY_REPORT)
}
