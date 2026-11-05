BeforeAll {
    $script:Repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

    function Invoke-FixtureReSharper {
        param([string] $Mode, [switch] $Staged, [string] $Path = 'src/Sample/Program.cs')
        [string[]] $selection = if ($Staged) { @('-Staged') } else { @('-Include', $Path) }
        $output = pwsh -NoProfile -File (Join-Path $script:Fixture 'scripts/resharper.ps1') -Mode $Mode @selection 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    }
}

Describe 'ReSharper command' {
    BeforeEach {
        $script:Fixture = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        foreach ($folder in @('scripts', '.config', 'src/Sample')) {
            New-Item -ItemType Directory -Path (Join-Path $script:Fixture $folder) -Force | Out-Null
        }
        foreach ($path in @('scripts/resharper.ps1', '.config/dotnet-tools.json', '.gitignore', '.editorconfig', 'global.json', 'winshell.slnx.DotSettings')) {
            Copy-Item -LiteralPath (Join-Path $script:Repository $path) -Destination (Join-Path $script:Fixture $path)
        }
        [IO.File]::WriteAllText((Join-Path $script:Fixture 'winshell.slnx'), '<Solution><Project Path="src/Sample/Sample.csproj" /></Solution>')
        [IO.File]::WriteAllText((Join-Path $script:Fixture 'src/Sample/Sample.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
        $script:Program = Join-Path $script:Fixture 'src/Sample/Program.cs'
        # Changing an expression-bodied collection return to a block changes which object-creation style applies.
        [IO.File]::WriteAllText($script:Program, 'namespace Sample; internal readonly record struct Item(int Value); internal static class Program{private static Item[] Items()=>[new(1),new(2)];public static void Main(){System.Console.WriteLine("staged" + Items().Length);}}')
        git -C $script:Fixture init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fixture repository.' }
        git -C $script:Fixture add .
        if ($LASTEXITCODE -ne 0) { throw 'Could not stage fixture files.' }
        $script:Index = Join-Path $script:Fixture '.git/index'
        $script:IndexHash = (Get-FileHash -LiteralPath $script:Index).Hash
    }

    It 'detects drift without altering partly staged files, then accepts a repeat cleanup' {
        [IO.File]::WriteAllText($script:Program, ([IO.File]::ReadAllText($script:Program).Replace('"staged"', '"working"')))
        $workingHash = (Get-FileHash -LiteralPath $script:Program).Hash

        $check = Invoke-FixtureReSharper Check -Staged
        $check.ExitCode | Should -Be 1 -Because $check.Output
        $check.Output | Should -Match 'ReSharper cleanup would change:'
        (Get-FileHash -LiteralPath $script:Program).Hash | Should -Be $workingHash
        (Get-FileHash -LiteralPath $script:Index).Hash | Should -Be $script:IndexHash

        $cleanup = Invoke-FixtureReSharper Cleanup
        $cleanup.ExitCode | Should -Be 0 -Because $cleanup.Output
        $cleanHash = (Get-FileHash -LiteralPath $script:Program).Hash
        $cleanHash | Should -Not -Be $workingHash
        [IO.File]::ReadAllText($script:Program) | Should -Match '"working"'

        $again = Invoke-FixtureReSharper Check -Staged
        $again.ExitCode | Should -Be 0 -Because $again.Output
        (Get-FileHash -LiteralPath $script:Program).Hash | Should -Be $cleanHash
        (Get-FileHash -LiteralPath $script:Index).Hash | Should -Be $script:IndexHash
    }

    It 'fails on a build error without changing working files or the index' {
        [IO.File]::WriteAllText($script:Program, 'not valid C#')
        $workingHash = (Get-FileHash -LiteralPath $script:Program).Hash
        $check = Invoke-FixtureReSharper Check
        $check.ExitCode | Should -Be 1 -Because $check.Output
        $check.Output | Should -Match 'dotnet build failed'
        (Get-FileHash -LiteralPath $script:Program).Hash | Should -Be $workingHash
        (Get-FileHash -LiteralPath $script:Index).Hash | Should -Be $script:IndexHash
    }

    It 'does not check unstaged source when only configuration files are staged' {
        git -C $script:Fixture rm --cached -- src/Sample/Program.cs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not unstage the sample source.' }
        $workingHash = (Get-FileHash -LiteralPath $script:Program).Hash
        $check = Invoke-FixtureReSharper Check -Staged
        $check.ExitCode | Should -Be 0 -Because $check.Output
        (Get-FileHash -LiteralPath $script:Program).Hash | Should -Be $workingHash
    }

    It 'keeps template inspection locations readable after temporary files are removed' {
        foreach ($folder in @('templates/winshell', 'src/Winshell.Shell')) {
            New-Item -ItemType Directory -Path (Join-Path $script:Fixture $folder) -Force | Out-Null
        }
        [IO.File]::WriteAllText((Join-Path $script:Fixture 'src/Winshell.Shell/Winshell.Shell.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        [IO.File]::WriteAllText((Join-Path $script:Fixture 'templates/winshell/MyShell.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>MyShell</RootNamespace><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include="Winshell" Version="0.0.0" /></ItemGroup></Project>')
        $templateSource = Join-Path $script:Fixture 'templates/winshell/Program.cs'
        [IO.File]::WriteAllText($templateSource, 'namespace MyShell; internal static class Program { public static void Main() { } private static void Unused() { } }')

        $inspection = Invoke-FixtureReSharper Inspect -Path templates/winshell/Program.cs
        $inspection.ExitCode | Should -Be 1 -Because $inspection.Output
        $report = Get-Content -LiteralPath (Join-Path $script:Fixture 'artifacts/resharper/template.sarif') -Raw | ConvertFrom-Json
        $run = $report.runs[0]
        @($run.results).Count | Should -BeGreaterThan 0
        $baseUri = [uri]$run.originalUriBaseIds.solutionDir.uri
        foreach ($result in $run.results) {
            $location = [uri]::new($baseUri, $result.locations[0].physicalLocation.artifactLocation.uri)
            $location.LocalPath | Should -Be $templateSource
            Test-Path -LiteralPath $location.LocalPath | Should -BeTrue
        }
        foreach ($artifact in $run.artifacts) {
            ([uri]::new($baseUri, $artifact.location.uri)).LocalPath | Should -Be $templateSource
        }
    }
}
