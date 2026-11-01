BeforeAll {
    . (Join-Path $PSScriptRoot 'ReleaseGate.ps1')
    function script:New-CiRun([string]$Status, [string]$Conclusion, [string]$StartedAt) {
        [pscustomobject]@{ name = 'ci'; status = $Status; conclusion = $Conclusion; started_at = $StartedAt }
    }
}

Describe 'Get-WinshellReleaseCiState' {
    It 'waits when this commit has no ci run' {
        Get-WinshellReleaseCiState -CheckRuns @() | Should -Be 'pending'
        Get-WinshellReleaseCiState -CheckRuns @([pscustomobject]@{ name = 'pr-title'; status = 'completed'; conclusion = 'success'; started_at = '2026-10-29T12:00:00Z' }) | Should -Be 'pending'
    }

    It 'waits while the newest ci run has not finished' {
        $runs = @(
            (New-CiRun 'completed' 'success' '2026-10-29T12:00:00Z'),
            (New-CiRun 'in_progress' $null '2026-10-29T12:05:00Z')
        )
        Get-WinshellReleaseCiState -CheckRuns $runs | Should -Be 'pending'
        Get-WinshellReleaseCiState -CheckRuns @((New-CiRun 'queued' $null $null)) | Should -Be 'pending'
    }

    It 'is green only when the newest finished ci run succeeded' {
        $runs = @(
            (New-CiRun 'completed' 'failure' '2026-10-29T12:00:00Z'),
            (New-CiRun 'completed' 'success' '2026-10-29T12:05:00Z')
        )
        Get-WinshellReleaseCiState -CheckRuns $runs | Should -Be 'green'
    }

    It 'is red when the newest finished ci run failed' {
        $runs = @(
            (New-CiRun 'completed' 'success' '2026-10-29T12:00:00Z'),
            (New-CiRun 'completed' 'failure' '2026-10-29T12:05:00Z')
        )
        Get-WinshellReleaseCiState -CheckRuns $runs | Should -Be 'red'
        Get-WinshellReleaseCiState -CheckRuns @((New-CiRun 'completed' 'cancelled' '2026-10-29T12:00:00Z')) | Should -Be 'red'
    }

    It 'waits when a later ci run is queued after an older success' {
        $runs = @(
            (New-CiRun 'completed' 'success' '2026-10-29T12:00:00Z'),
            (New-CiRun 'queued' $null $null)
        )
        Get-WinshellReleaseCiState -CheckRuns $runs | Should -Be 'pending'
    }
}

Describe 'Get-WinshellReleaseGateState' {
    It 'waits when the check runs cannot be read' {
        Mock Get-WinshellCheckRuns { throw 'Could not read check runs for abc' }
        Mock Write-Warning { }
        Get-WinshellReleaseGateState -Repository 'o/r' -Sha 'abc' | Should -Be 'pending'
    }

    It 'gives the state of the check runs it can read' {
        Mock Get-WinshellCheckRuns { @(New-CiRun 'completed' 'failure' '2026-10-29T12:00:00Z') }
        Get-WinshellReleaseGateState -Repository 'o/r' -Sha 'abc' | Should -Be 'red'
    }
}