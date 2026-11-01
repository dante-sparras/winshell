# Decides whether a release may publish from the ci check runs on its commit.
# The workflow waits while the result is pending, and stops when it is red.

function Get-WinshellReleaseCiState {
    param($CheckRuns)

    $ci = @($CheckRuns | Where-Object { $_ -and $_.name -eq 'ci' })
    if ($ci.Count -eq 0) { return 'pending' }

    $latest = $ci | Sort-Object -Property @{
        Expression = {
            if ($_.started_at) { [datetimeoffset]::Parse([string]$_.started_at) }
            else { [datetimeoffset]::MaxValue }
        }
    } -Descending | Select-Object -First 1

    if ([string]$latest.status -ne 'completed') { return 'pending' }
    if ([string]$latest.conclusion -eq 'success') { return 'green' }
    return 'red'
}

function Get-WinshellCheckRuns {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Sha
    )

    $runs = [System.Collections.Generic.List[object]]::new()
    $page = 1
    do {
        $json = gh api "repos/$Repository/commits/$Sha/check-runs?per_page=100&page=$page"
        if ($LASTEXITCODE -ne 0) { throw "Could not read check runs for $Sha" }
        $response = $json | ConvertFrom-Json
        $batch = if ($null -eq $response.check_runs) { @() } else { @($response.check_runs) }
        foreach ($run in $batch) { if ($null -ne $run) { $runs.Add($run) } }
        $page++
    } while ($batch.Count -eq 100)
    return $runs
}

# One failed read is not a result, so the workflow asks again until its deadline.
function Get-WinshellReleaseGateState {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Sha
    )

    try {
        return Get-WinshellReleaseCiState -CheckRuns (Get-WinshellCheckRuns -Repository $Repository -Sha $Sha)
    }
    catch {
        Write-Warning "Reading ci on $Sha failed, so this check waits: $_"
        return 'pending'
    }
}