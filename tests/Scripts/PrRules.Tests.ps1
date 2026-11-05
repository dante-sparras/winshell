BeforeAll {
    . (Join-Path $PSScriptRoot '../../scripts/pr-rules.ps1')
    $script:Proof = "- **Before:** the test failed with Expected: 1, Actual: 0.`n  **After:** it passes."
}

Describe 'Get-PrProblem' {
    It 'asks nothing of a build, ci, docs or refactor change' {
        foreach ($title in 'build: x', 'ci: x', 'docs: x', 'refactor!: x', 'chore(main): release 0.8.0', 'test: x', 'Revert "fix: x"') {
            Get-PrProblem -Title $title -Body '' -Files @('src/Winshell/Program.cs', 'src/Winshell.Shell/Panel.xaml') | Should -BeNullOrEmpty -Because $title
        }
    }

    It 'wants a test in a fix' {
        Get-PrProblem -Title 'fix(cli): x' -Body $script:Proof -Files @('src/Winshell/Program.cs') | Should -Match 'A fix needs a test'
    }

    It 'takes a reason why a fix can have no test, and not an empty one' {
        Get-PrProblem -Title 'fix(package): x' -Body '**No test:** only a release can run this script.' -Files @('scripts/package.ps1') | Should -BeNullOrEmpty
        Get-PrProblem -Title 'fix(package): x' -Body "**No test:**`n$script:Proof" -Files @('scripts/package.ps1') | Should -Match 'No test'
    }

    It 'wants the fix to say how its test failed' {
        Get-PrProblem -Title 'fix: x' -Body 'It works now.' -Files @('src/A.cs', 'tests/Winshell.Tests/ATests.cs') | Should -Match 'how its test failed'
        Get-PrProblem -Title 'fix: x' -Body "- **Before:**`n  **After:**" -Files @('src/A.cs', 'tests/Winshell.Tests/ATests.cs') | Should -Match 'how its test failed'
    }

    It 'takes a fix with a test and a before and after' {
        Get-PrProblem -Title 'fix(cli): x' -Body $script:Proof -Files @('src/A.cs', 'tests/Winshell.Tests/ATests.cs') | Should -BeNullOrEmpty
    }

    It 'takes a Pester test as a fix to a script' {
        Get-PrProblem -Title 'fix: x' -Body $script:Proof -Files @('scripts/install.ps1', 'tests/Scripts/Install.Tests.ps1') | Should -BeNullOrEmpty
    }

    It 'asks no test of a fix to docs' {
        Get-PrProblem -Title 'fix(docs): a broken link' -Body '' -Files @('docs/configuration.md', 'README.md') | Should -BeNullOrEmpty
    }

    It 'wants numbers in a perf change' {
        Get-PrProblem -Title 'perf(shell): x' -Body 'Faster.' -Files @('src/A.cs') | Should -Match 'each with a number'
        Get-PrProblem -Title 'perf: x' -Body "**Before:** slow`n**After:** fast" -Files @('src/A.cs') | Should -Match 'each with a number'
        Get-PrProblem -Title 'perf(shell)!: x' -Body "**Before:** 234 ms`n**After:** 141 ms" -Files @('src/A.cs') | Should -BeNullOrEmpty
    }

    It 'does not let one blank line borrow from the next' {
        Get-PrProblem -Title 'perf: x' -Body "**Before:**`n**After:** 141 ms`n`nOther text, 12 of it." -Files @('src/A.cs') | Should -Match 'each with a number'
    }

    It 'takes numbers in a table' {
        $table = "| | Before | After |`r`n| --- | ---: | ---: |`r`n| Idle CPU | 797 ms | 141 ms |"
        Get-PrProblem -Title 'perf(shell): x' -Body $table -Files @('src/A.cs') | Should -BeNullOrEmpty
    }

    It 'reads every row of a table, and one under a bullet' {
        $table = "- Measured:`n  | | Before | After |`n  | --- | --- | --- |`n  | Build | release | release |`n  | Idle CPU | 797 ms | 141 ms |"
        Get-PrProblem -Title 'perf(shell): x' -Body $table -Files @('src/A.cs') | Should -BeNullOrEmpty
    }

    It 'does not take an empty table for how a test failed' {
        Get-PrProblem -Title 'fix: x' -Body "| Before | After |`n| --- | --- |`n| | |" -Files @('src/A.cs', 'tests/Winshell.Tests/ATests.cs') | Should -Match 'how its test failed'
    }

    It 'wants numbers in a table too' {
        $table = "| | Before | After |`n| --- | --- | --- |`n| Idle CPU | high | low |"
        Get-PrProblem -Title 'perf(shell): x' -Body $table -Files @('src/A.cs') | Should -Match 'each with a number'
    }

    It 'wants a picture of a change to XAML' {
        Get-PrProblem -Title 'feat: x' -Body 'Words.' -Files @('src/Winshell.Shell/Panel.xaml') | Should -Match 'screenshot'
    }

    It 'takes a picture, or a reason there is none' {
        Get-PrProblem -Title 'feat: x' -Body '![Start](https://example.com/start.png)' -Files @('src/A.xaml') | Should -BeNullOrEmpty
        Get-PrProblem -Title 'feat: x' -Body '**Not visual:** it only renames a resource key.' -Files @('src/A.xaml') | Should -BeNullOrEmpty
    }

    It 'takes a picture dropped into the page' {
        Get-PrProblem -Title 'feat: x' -Body '<img width="400" alt="Start" src="https://example.com/start.png" />' -Files @('src/A.xaml') | Should -BeNullOrEmpty
    }

    It 'does not count an empty reason' {
        Get-PrProblem -Title 'feat: x' -Body '**Not visual:**' -Files @('src/A.xaml') | Should -Match 'screenshot'
    }

    It 'does not count what the template left in a comment' {
        Get-PrProblem -Title 'feat: x' -Body '**Not visual:** <!-- Why there is no picture. -->' -Files @('src/A.xaml') | Should -Match 'screenshot'
        Get-PrProblem -Title 'fix: x' -Body "- **Before:** <!-- It failed. -->`n  **After:** <!-- It passes. -->" -Files @('src/A.cs', 'tests/Winshell.Tests/ATests.cs') | Should -Match 'how its test failed'
    }

    It 'holds the template itself to the rules' {
        $template = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../../.github/pull_request_template.md')
        @(Get-PrProblem -Title 'fix: x' -Body $template -Files @('src/A.xaml', 'tests/Winshell.Tests/ATests.cs')).Count | Should -Be 2
    }

    It 'looks only at XAML in src' {
        Get-PrProblem -Title 'feat: x' -Body '' -Files @('tests/Winshell.Shell.Tests/Sample.xaml', 'templates/winshell/PanelView.xaml') | Should -BeNullOrEmpty
    }

    It 'says each problem of a change with several' {
        @(Get-PrProblem -Title 'fix: x' -Body '' -Files @('src/A.xaml')).Count | Should -Be 3
    }
}

Describe 'The check' {
    BeforeAll {
        $script:Paths = Join-Path $TestDrive 'files.txt'
        Set-Content -LiteralPath $script:Paths -Value 'src/Winshell/Program.cs'
        $script:Rules = Join-Path $PSScriptRoot '../../scripts/pr-rules.ps1'
    }

    It 'fails a pull request with a problem, and says what it is' {
        $output = pwsh -NoProfile -File $script:Rules -Title 'fix: x' -Body '' -Path $script:Paths
        $LASTEXITCODE | Should -Be 1
        $output | Should -Contain "::error title=Pull request::A fix says under Verification how its test failed before the fix: **Before:** and **After:**."
    }

    It 'passes one with none' {
        pwsh -NoProfile -File $script:Rules -Title 'ci: x' -Body '' -Path $script:Paths | Should -BeNullOrEmpty
        $LASTEXITCODE | Should -Be 0
    }
}
