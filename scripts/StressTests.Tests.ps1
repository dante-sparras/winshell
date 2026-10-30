BeforeAll {
    . (Join-Path $PSScriptRoot 'stress-tests.ps1')

    function New-Run {
        param([int] $Exit = 0, [string[]] $Failed = @(), [string[]] $Passed = @('Ns.Class.One'), [bool] $Complete = $true)
        [pscustomobject]@{
            Exit = $Exit
            Output = @('test output')
            Result = [pscustomobject]@{ Failed = $Failed; Passed = $Passed; Complete = $Complete }
        }
    }

    function New-Test {
        param([string] $Name = 'One', [string] $Project = 'tests/Winshell.Runtime.Tests', [bool] $Isolated = $false)
        [pscustomobject]@{ Name = $Name; Project = $Project; Isolated = $Isolated }
    }
}

Describe 'Reading a diff' {
    It 'finds added and edited lines by their number after the change' {
        $changed = Read-ChangedLine -Diff @(
            'diff --git a/tests/A.cs b/tests/A.cs',
            '--- a/tests/A.cs',
            '+++ b/tests/A.cs',
            '@@ -3 +3 @@ class A',
            '-old',
            '+new',
            '@@ -10,0 +11,2 @@',
            '+one',
            '+two')
        @($changed['tests/A.cs'] | Sort-Object) | Should -Be @(3, 11, 12)
    }

    It 'marks the gap for a hunk that only deletes' {
        $changed = Read-ChangedLine -Diff @('+++ b/tests/A.cs', '@@ -5,2 +4,0 @@', '-gone', '-gone')
        @($changed['tests/A.cs']) | Should -Be @(4.5)
    }

    It 'skips a deleted file' {
        $changed = Read-ChangedLine -Diff @('--- a/tests/A.cs', '+++ /dev/null', '@@ -1,2 +0,0 @@', '-gone', '-gone')
        $changed.Count | Should -Be 0
    }

    It 'reads an empty diff' {
        (Read-ChangedLine).Count | Should -Be 0
    }
}

Describe 'Reading tests from source' {
    BeforeAll {
        $script:Source = @(
            'public class Things',                  # 1
            '{',                                    # 2
            '    [Fact]',                           # 3
            '    public void First_test()',         # 4
            '    {',                                # 5
            '    }',                                # 6
            '',                                     # 7
            '    [Trait("Kind", "x")]',             # 8
            '    [Theory]',                         # 9
            '    [InlineData(1)]',                  # 10
            '    public async Task Second_test(int value)', # 11
            '    {',                                # 12
            '    }',                                # 13
            '}')                                    # 14
    }

    It 'names each test and leaves the blank line between tests to neither' {
        $tests = @(Read-TestMethod -Source $script:Source)
        $tests.Name | Should -Be @('First_test', 'Second_test')
        $tests[0].First | Should -Be 3
        $tests[0].Last | Should -Be 6
    }

    It 'starts a test at its first attribute and ends it at its closing brace' {
        $tests = @(Read-TestMethod -Source $script:Source)
        $tests[1].First | Should -Be 8
        $tests[1].Last | Should -Be 13
    }

    It 'ends a test with no block at its semicolon' {
        $tests = @(Read-TestMethod -Source @(
                '    [Fact]',
                '    public void Short()',
                '        => Assert.True(true);',
                '',
                '    [Fact]',
                '    public void Next()',
                '    {',
                '    }'))
        $tests.Name | Should -Be @('Short', 'Next')
        $tests[0].Last | Should -Be 3
    }

    It 'ends a test whose body is a lambda at the end of the lambda' {
        $tests = @(Read-TestMethod -Source @(
                '    [Fact]',
                '    public void Themed() => WithTheme(() =>',
                '    {',
                '        Assert.True(true);',
                '    });',
                '',
                '    [Fact]',
                '    public void Next()',
                '    {',
                '    }'))
        $tests.Name | Should -Be @('Themed', 'Next')
        $tests[0].Last | Should -Be 5
    }

    It 'finds an internal test, because xUnit runs those too' {
        $tests = @(Read-TestMethod -Source @(
                '    [Theory]',
                '    [InlineData(1)]',
                '    internal void Hidden(int value) =>',
                '        Assert.Equal(1, value);'))
        $tests.Name | Should -Be @('Hidden')
        $tests[0].Last | Should -Be 4
    }

    It 'is not fooled by a closing brace deeper than the method' {
        $tests = @(Read-TestMethod -Source @(
                '    [Fact]',
                '    public void Quoted()',
                '    {',
                '        var qml = """',
                '            Item {',
                '            }',
                '            """;',
                '    }',
                '}'))
        $tests[0].Last | Should -Be 8
    }

    It 'finds nothing in a file without tests' {
        @(Read-TestMethod -Source @('public class Helper', '{', '}')).Count | Should -Be 0
    }

    It 'refuses an attribute with no method after it' {
        { Read-TestMethod -Source @('    [Fact]', '}') } | Should -Throw '*line 1*'
    }
}

Describe 'Finding the changed tests' {
    BeforeEach {
        $script:SavedRoot = $script:Root
        $script:Root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $folder = Join-Path $script:Root 'tests\Winshell.Runtime.Tests'
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $script:File = Join-Path $folder 'ThingTests.cs'
        function script:Commit-All {
            git -C $script:Root add -A
            git -C $script:Root -c user.name=test -c user.email=test@example.com commit -q -m change
        }
        git -C $script:Root init -q
        Set-Content -LiteralPath $script:File -Value @(
            'public class ThingTests', '{',
            '    [Fact]', '    public void Kept()', '    {', '    }', '',
            '    [Fact]', '    public void Edited()', '    {', '        var a = 1;', '    }', '}')
        Set-Content -LiteralPath (Join-Path $folder 'Other.cs') -Value @('public class Other', '{', '}')
        Commit-All
    }

    AfterEach {
        $script:Root = $script:SavedRoot
    }

    It 'finds an edited test and leaves the others out' {
        (Get-Content -LiteralPath $script:File) -replace 'var a = 1', 'var a = 2' | Set-Content -LiteralPath $script:File
        Commit-All
        $tests = @(Get-ChangedTest -Base 'HEAD^1')
        $tests.Name | Should -Be @('Edited')
        $tests[0].Project | Should -Be 'tests/Winshell.Runtime.Tests'
        $tests[0].Isolated | Should -BeFalse
    }

    It 'finds a new test' {
        (Get-Content -LiteralPath $script:File | Select-Object -SkipLast 1) + @('', '    [Theory]', '    [InlineData(1)]', '    public void Added(int a)', '    {', '    }', '}') |
            Set-Content -LiteralPath $script:File
        Commit-All
        @(Get-ChangedTest -Base 'HEAD^1').Name | Should -Be @('Added')
    }

    It 'finds a test whose lines were only deleted' {
        (Get-Content -LiteralPath $script:File) -notmatch 'var a = 1' | Set-Content -LiteralPath $script:File
        Commit-All
        @(Get-ChangedTest -Base 'HEAD^1').Name | Should -Be @('Edited')
    }

    It 'does not count a test that is deleted as a change to the tests around it' {
        (Get-Content -LiteralPath $script:File | Select-Object -First 7) + @('}') | Set-Content -LiteralPath $script:File
        Commit-All
        @(Get-ChangedTest -Base 'HEAD^1').Count | Should -Be 0
    }

    It 'does not count a helper after a test' {
        (Get-Content -LiteralPath $script:File | Select-Object -SkipLast 1) + @('', '    private static void Helper()', '    {', '    }', '}') |
            Set-Content -LiteralPath $script:File
        Commit-All
        @(Get-ChangedTest -Base 'HEAD^1').Count | Should -Be 0
    }

    It 'marks a test in a file of a category ci runs alone' {
        $text = (Get-Content -LiteralPath $script:File) -replace 'var a = 1', 'var a = 2'
        @('[Trait("Category", "Media")]') + $text | Set-Content -LiteralPath $script:File
        Commit-All
        $tests = @(Get-ChangedTest -Base 'HEAD^1')
        $tests.Name | Should -Be @('Edited')
        $tests[0].Isolated | Should -BeTrue
    }

    It 'stresses a test in a file with any other category' {
        $text = (Get-Content -LiteralPath $script:File) -replace 'var a = 1', 'var a = 2'
        @('[Trait("Category", "Slow")]') + $text | Set-Content -LiteralPath $script:File
        Commit-All
        @(Get-ChangedTest -Base 'HEAD^1')[0].Isolated | Should -BeFalse
    }

    It 'finds nothing when only a file without tests changed' {
        Add-Content -LiteralPath (Join-Path $script:Root 'tests\Winshell.Runtime.Tests\Other.cs') -Value '// more'
        Commit-All
        @(Get-ChangedTest -Base 'HEAD^1').Count | Should -Be 0
    }

    It 'stops on a base git does not know' {
        { Get-ChangedTest -Base 'no-such-ref' } | Should -Throw '*no-such-ref*'
    }
}

Describe 'Stressing the tests' {
    BeforeEach {
        Mock Start-CpuLoad { @('load') }
        Mock Stop-CpuLoad { }
        Mock Invoke-StressRound { New-Run }
        Mock Write-Host { }
    }

    It 'runs every round and passes' {
        Invoke-StressTest -Tests @(New-Test) -Rounds 30 -BudgetMinutes 15 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 30 -Exactly
        Should -Invoke Stop-CpuLoad -Times 1 -Exactly
    }

    It 'runs nothing and starts no load when no test changed' {
        Invoke-StressTest -Tests @() -Rounds 30 -BudgetMinutes 15 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 0 -Exactly
        Should -Invoke Start-CpuLoad -Times 0 -Exactly
    }

    It 'stops at the first failure, names the test, and ends the load' {
        $script:Round = 0
        Mock Invoke-StressRound {
            $script:Round++
            if ($script:Round -eq 3) { return New-Run -Exit 1 -Failed @('Ns.Class.One') -Passed @() }
            New-Run
        }
        Invoke-StressTest -Tests @(New-Test) -Rounds 30 -BudgetMinutes 15 | Should -Be 1
        Should -Invoke Invoke-StressRound -Times 3 -Exactly
        Should -Invoke Stop-CpuLoad -Times 1 -Exactly
        Should -Invoke Write-Host -ParameterFilter { $Object -like '::error*Ns.Class.One failed in round 3 of 30*' }
    }

    It 'fails a run that crashed without naming a test' {
        Mock Invoke-StressRound { New-Run -Exit 7 -Passed @() -Complete $false }
        Invoke-StressTest -Tests @(New-Test) -Rounds 30 -BudgetMinutes 15 | Should -Be 7
    }

    It 'fails a run that wrote no results even when it exits 0' {
        Mock Invoke-StressRound { New-Run -Passed @() -Complete $false }
        Invoke-StressTest -Tests @(New-Test) -Rounds 30 -BudgetMinutes 15 | Should -Be 1
    }

    It 'fails a test it found but dotnet did not run' {
        Mock Invoke-StressRound { New-Run -Passed @('Ns.Class.Other') }
        Invoke-StressTest -Tests @(New-Test) -Rounds 30 -BudgetMinutes 15 | Should -Be 1
    }

    It 'leaves out a test in a category ci runs alone' {
        Invoke-StressTest -Tests @(New-Test -Isolated $true) -Rounds 30 -BudgetMinutes 15 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 0 -Exactly
    }

    It 'leaves out a quarantined test and keeps it out of the filter' {
        Invoke-StressTest -Tests @((New-Test), (New-Test -Name 'Listed')) -Quarantined @('Ns.Class.Listed') -Rounds 1 -BudgetMinutes 15 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 1 -Exactly -ParameterFilter {
            $Filter -like '*FullyQualifiedName!=Ns.Class.Listed*' -and $Filter -like '*)&(FullyQualifiedName~.One)' -and $Filter -notlike '*~.Listed*'
        }
    }

    It 'selects each test by name inside the main filter' {
        Mock Invoke-StressRound { New-Run -Passed @('Ns.Class.One', 'Ns.Class.Two') }
        Invoke-StressTest -Tests @((New-Test), (New-Test -Name 'Two')) -Rounds 1 -BudgetMinutes 15 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 1 -Exactly -ParameterFilter {
            $Filter -like '(Category!=Performance&*)&(FullyQualifiedName~.One|FullyQualifiedName~.Two)'
        }
    }

    It 'runs each project on its own' {
        Mock Invoke-StressRound { New-Run -Passed @('Ns.Class.One', 'Ns.Class.Two') }
        Invoke-StressTest -Tests @((New-Test), (New-Test -Name 'Two' -Project 'tests/Winshell.Tests')) -Rounds 2 -BudgetMinutes 15 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 2 -Exactly -ParameterFilter { $Project -eq 'tests/Winshell.Tests' }
        Should -Invoke Invoke-StressRound -Times 2 -Exactly -ParameterFilter { $Project -eq 'tests/Winshell.Runtime.Tests' }
    }

    It 'stops at the budget with a warning and still passes' {
        Invoke-StressTest -Tests @(New-Test) -Rounds 30 -BudgetMinutes 0 | Should -Be 0
        Should -Invoke Invoke-StressRound -Times 1 -Exactly
        Should -Invoke Write-Host -ParameterFilter { $Object -like '::warning*after 1 of 30 rounds*' }
    }
}
