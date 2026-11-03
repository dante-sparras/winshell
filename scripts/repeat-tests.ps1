# Runs the test classes a pull request changed many times over, each time in another order. Every run must pass and run
# the same tests, so a test that only passes sometimes never reaches main. The classes come from a comma-separated list
# of changed paths, as ci's changes job gives them. A class in another tier matches nothing here, and the other job
# repeats it.
#
#   pwsh scripts/repeat-tests.ps1 -Tests tests/Winshell.Tests/CliHelpTests.cs -Tier Slow -Times 5
param(
    [string] $Tests = '',
    [ValidateSet('All', 'Fast', 'Slow')]
    [string] $Tier = 'Fast',
    [int] $Times = 20
)

# Its param block binds here, so pass the tier on or it comes back as that script's default.
. (Join-Path $PSScriptRoot 'run-tests.ps1') -Tier $Tier

# Each project's changed classes, as a dotnet test filter.
function Get-RepeatTarget {
    param([string] $Tests = '', [string] $Tier = 'Fast')
    $classes = @(($Tests -replace '\\', '/') -split ',' | ForEach-Object {
            if (($_ -replace '^\./') -match '^tests/(Winshell(?:\.\w+)?\.Tests)/(?:.*/)?(\w+Tests)\.cs$' -and $Matches[1] -ne 'Winshell.Package.Tests') {
                [pscustomobject]@{ Project = $Matches[1]; Class = $Matches[2] }
            }
        })
    foreach ($group in $classes | Group-Object Project) {
        $names = @($group.Group | ForEach-Object { "FullyQualifiedName~.$($_.Class)." }) -join '|'
        [pscustomobject]@{ Project = $group.Name; Filter = "($names)&$(Get-MainTestFilter -Tier $Tier)" }
    }
}

# Passes when each run passed and all of them ran the same tests.
function Compare-Repeat {
    param([Parameter(Mandatory)][object[]] $Runs)
    $first = @($Runs[0].Passed | Sort-Object) -join "`n"
    foreach ($run in $Runs) {
        if ($run.Failed.Count -gt 0) { return "Failed in at least one run: $($run.Failed -join ', ')" }
        if (-not $run.Complete) { return 'A run did not finish.' }
        if ($run.Results -ne $Runs[0].Results -or (@($run.Passed | Sort-Object) -join "`n") -cne $first) { return 'The runs did not all run the same tests.' }
    }
    $null
}

function Invoke-Repeat {
    param([Parameter(Mandatory)][object] $Target, [int] $Times = 20)
    $results = Join-Path ([IO.Path]::GetTempPath()) ('winshell-repeat-' + [guid]::NewGuid().ToString('N'))
    try {
        $runs = [Collections.Generic.List[object]]::new()
        foreach ($i in 1..$Times) {
            $folder = Join-Path $results "$i"
            $seed = Get-Random -Minimum 1 -Maximum ([int]::MaxValue)
            Write-Host "Run $i of $Times, with seed $seed."
            $exit = Invoke-DotnetTest -Filter $Target.Filter -ResultsDirectory $folder -Target (Join-Path $script:Root "tests/$($Target.Project)") -Seed $seed
            $run = Read-TestResult -Directory $folder -ProjectCount 1
            if ($exit -ne 0) { return "Run $i of $Times failed: $(if ($run.Failed) { $run.Failed -join ', ' } else { 'it did not finish' })" }
            $runs.Add($run)
            # Classes of the other tier match nothing, so there is nothing to repeat.
            if ($run.Results -eq 0) { break }
        }
        $problem = Compare-Repeat -Runs $runs.ToArray()
        if (-not $problem) { Write-Host "$($Target.Project): $($runs.Count) runs of $($runs[0].Results) tests agree." }
        $problem
    }
    finally {
        Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $targets = @(Get-RepeatTarget -Tests $Tests -Tier $Tier)
    if ($targets.Count -eq 0) { Write-Host 'No test class to repeat.' }
    foreach ($target in $targets) {
        $problem = Invoke-Repeat -Target $target -Times $Times
        if ($problem) {
            Write-Host "$($target.Project): $problem"
            exit 1
        }
    }
}
