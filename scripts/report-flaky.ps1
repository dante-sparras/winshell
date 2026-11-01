#requires -Version 7.0
# Files an issue labeled flaky for each test in a run-tests.ps1 report, or comments on the one already open.
# See "Flaky tests" in CONTRIBUTING.md.
param(
    [string] $ReportPath,
    [string] $RunUrl
)

function Publish-FlakyTest {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $RunUrl
    )
    $title = "Flaky test: $Name"
    $found = gh issue list --state open --label flaky --search "`"$Name`" in:title" --json number,title --limit 50
    if ($LASTEXITCODE -ne 0) { throw "Could not search for the issue of $Name." }
    $issue = @($found | ConvertFrom-Json | Where-Object { $_.title -ceq $title }) | Select-Object -First 1
    if ($issue) {
        gh issue comment $issue.number --body "It failed in one run but not in others again: $RunUrl"
        if ($LASTEXITCODE -ne 0) { throw "Could not comment on issue $($issue.number)." }
        return
    }
    $body = @"
``$Name`` failed in one run of the ``ci`` job's tests but not in others, so the job passed: $RunUrl

Find the cause and fix it. If it keeps failing while that happens, add its name to ``tests/quarantine.txt`` in a pull request. See "Flaky tests" in CONTRIBUTING.md.
"@
    gh issue create --title $title --label flaky --body $body
    if ($LASTEXITCODE -ne 0) { throw "Could not file an issue for $Name." }
}

function Publish-FlakyReport {
    param(
        [Parameter(Mandatory)][string] $ReportPath,
        [Parameter(Mandatory)][string] $RunUrl
    )
    foreach ($name in @(Get-Content -LiteralPath $ReportPath | Where-Object { $_ })) {
        Publish-FlakyTest -Name $name -RunUrl $RunUrl
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    Publish-FlakyReport -ReportPath $ReportPath -RunUrl $RunUrl
}
