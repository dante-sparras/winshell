# What a pull request must carry for its kind of change, read from its title, body and files. The pr-rules job runs this
# from the base branch, so a pull request can't change the rules it is held to.
#
#   pwsh scripts/pr-rules.ps1 -Title <title> -Body <body> -Path <file of changed paths>
#
# prints each problem and exits 1. Only fix, perf and feat have rules of their own.
param(
    [string] $Title,
    [string] $Body,
    [string] $Path
)

# Whether the body gives a before and an after with what $Value matches: on **Before:** and **After:** lines, or anywhere in
# the rows of a table with those headings.
function Test-BeforeAfter {
    param([string] $Body, [string] $Value)
    ($Body -match "\*\*Before:\*\*[^\r\n]*$Value" -and $Body -match "\*\*After:\*\*[^\r\n]*$Value") -or
    $Body -match "(?m)^[ \t]*\|.*\bBefore\b.*\bAfter\b.*\n[ \t]*\|[-:| ]+\r?\n(?:[ \t]*\|.*\n)*[ \t]*\|.*$Value"
}

function Get-PrProblem {
    param(
        [string] $Title = '',
        [string] $Body = '',
        [string[]] $Files = @()
    )
    # The template's hints are comments, and a hint is not an answer.
    $Body = $Body -replace '(?s)<!--.*?-->', ''
    $changed = @($Files | Where-Object { $_ })
    $type = ($Title -split '[(:!]', 2)[0]
    $code = @($changed | Where-Object { $_ -notmatch '^docs/|\.md$' })
    $tests = @($changed | Where-Object { $_ -match '^tests/.*\.cs$|^tests/Scripts/.*\.Tests\.ps1$' })
    if ($type -eq 'fix' -and $code.Count -gt 0 -and $Body -notmatch '\*\*No test:\*\*[ \t]*\S') {
        if ($tests.Count -eq 0) {
            'A fix needs a test that failed before it, or a **No test:** line that says why it can''t have one.'
        }
        if (-not (Test-BeforeAfter -Body $Body -Value '[^\s|]')) {
            'A fix says under Evidence how its test failed before the fix: **Before:** and **After:**.'
        }
    }
    if ($type -eq 'perf' -and -not (Test-BeforeAfter -Body $Body -Value '\d')) {
        'A perf change gives numbers under Evidence: **Before:** and **After:**, each with a number, or a table with those headings.'
    }
    if ($type -in 'feat', 'fix', 'perf' -and @($changed | Where-Object { $_ -match '^src/.*\.xaml$' }).Count -gt 0 -and $Body -notmatch '!\[[^\]]*\]\(\S+\)|<img\s|\*\*Not visual:\*\*[ \t]*\S') {
        'A change to XAML shows a winshell inspect screenshot in the PR, or says why not on a **Not visual:** line.'
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $problems = @(Get-PrProblem -Title $Title -Body $Body -Files @(Get-Content -LiteralPath $Path))
    foreach ($problem in $problems) { Write-Output "::error title=Pull request::$problem" }
    if ($problems.Count -gt 0) { exit 1 }
}
