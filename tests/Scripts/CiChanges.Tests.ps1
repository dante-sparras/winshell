BeforeAll {
    $script:Classifier = (Resolve-Path (Join-Path $PSScriptRoot '../../scripts/ci-changes.ps1')).Path
}

Describe 'Selecting CI checks from GitHub changed files' {
    It '<Name>' -ForEach @(
        @{ Name = 'keeps agent prose to link checks'; Path = 'AGENTS.md'; Text = '# Instructions'; Patch = '+A sentence'; Fast = $false; Docs = $false },
        @{ Name = 'keeps guide prose to link checks'; Path = 'docs/development.md'; Text = '# Development'; Patch = '+A sentence'; Fast = $false; Docs = $false },
        @{ Name = 'keeps script documentation out of installer checks'; Path = 'scripts/README.md'; Text = '# Scripts'; Patch = '+A sentence'; Fast = $false; Docs = $false },
        @{ Name = 'keeps action documentation out of tooling checks'; Path = '.github/actions/README.md'; Text = '# Actions'; Patch = '+A sentence'; Fast = $false; Docs = $false },
        @{ Name = 'keeps a deleted prose page to link checks'; Path = 'docs/privacy.md'; Text = $null; Patch = '-# Privacy'; Fast = $false; Docs = $false },
        @{ Name = 'checks a page containing compiled examples'; Path = 'docs/configuration.md'; Text = "``````csharp`nShell.Run();`n``````"; Patch = '+A sentence'; Fast = $false; Docs = $true },
        @{ Name = 'checks a new README example'; Path = 'README.md'; Text = "``````csharp`nShell.Run();`n``````"; Patch = '+```csharp'; Fast = $false; Docs = $true },
        @{ Name = 'checks a removed last C# fence'; Path = 'README.md'; Text = '# README'; Patch = '-```csharp'; Fast = $false; Docs = $true },
        @{ Name = 'checks a deleted example page'; Path = 'docs/example.md'; Text = $null; Patch = '-```csharp'; Fast = $false; Docs = $true },
        @{ Name = 'checks generated reference changes'; Path = 'docs/reference/winshell-audio.md'; Text = '# Audio'; Patch = '+A member'; Fast = $false; Docs = $true },
        @{ Name = 'checks documentation when GitHub omits a patch'; Path = 'README.md'; Text = '# README'; Patch = $null; Fast = $false; Docs = $true },
        @{ Name = 'leaves agent examples outside the application sample contract'; Path = 'docs/agents/domain.md'; Text = '```csharp'; Patch = '+```csharp'; Fast = $false; Docs = $false },
        @{ Name = 'runs the suite for application changes'; Path = 'src/Winshell/Program.cs'; Text = ''; Patch = '+code'; Fast = $true; Docs = $false }
    ) {
        $checkout = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $checkout | Out-Null
        if ($null -ne $Text) {
            $target = Join-Path $checkout $Path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Set-Content -LiteralPath $target -Value $Text
        }
        $json = Join-Path $TestDrive 'changed.json'
        ConvertTo-Json -InputObject @(@{ filename = $Path; patch = $Patch }) | Set-Content -LiteralPath $json
        Push-Location $checkout
        try { $output = & $script:Classifier -Files $json }
        finally { Pop-Location }
        $output | Should -Contain "fast=$($Fast.ToString().ToLowerInvariant())"
        $output | Should -Contain "docs=$($Docs.ToString().ToLowerInvariant())"
        $output | Should -Contain "code=$($Fast.ToString().ToLowerInvariant())"
        $output | Should -Contain 'scripts=false'
        $output | Should -Contain 'workflows=false'
    }

    It 'retains full checks when a source file is renamed into documentation' {
        $json = Join-Path $TestDrive 'rename.json'
        ConvertTo-Json -InputObject @(@{ filename = 'docs/example.md'; previous_filename = 'src/Example.cs'; patch = '' }) | Set-Content -LiteralPath $json
        $output = & $script:Classifier -Files $json
        $output | Should -Contain 'fast=true'
        $output | Should -Contain 'code=true'
    }

    It 'retains full checks when prose and application code change together' {
        $json = Join-Path $TestDrive 'mixed.json'
        ConvertTo-Json -InputObject @(
            @{ filename = 'AGENTS.md'; patch = '+prose' },
            @{ filename = 'src/Winshell/Program.cs'; patch = '+code' }
        ) | Set-Content -LiteralPath $json
        $output = & $script:Classifier -Files $json
        $output | Should -Contain 'fast=true'
        $output | Should -Contain 'code=true'
    }

    It 'checks examples renamed out of the guide even when the patch changes only prose' {
        $checkout = Join-Path $TestDrive 'moved-example'
        New-Item -ItemType Directory -Path $checkout | Out-Null
        Set-Content -LiteralPath (Join-Path $checkout 'moved.md') -Value '```csharp'
        $json = Join-Path $TestDrive 'moved.json'
        ConvertTo-Json -InputObject @(@{ filename = 'moved.md'; previous_filename = 'docs/example.md'; patch = '+prose' }) | Set-Content -LiteralPath $json
        Push-Location $checkout
        try { $output = & $script:Classifier -Files $json }
        finally { Pop-Location }
        $output | Should -Contain 'fast=false'
        $output | Should -Contain 'docs=true'
    }

    It 'retains tooling checks for workflow changes' {
        $json = Join-Path $TestDrive 'workflow.json'
        ConvertTo-Json -InputObject @(@{ filename = '.github/workflows/ci.yml'; patch = '' }) | Set-Content -LiteralPath $json
        $output = & $script:Classifier -Files $json
        $output | Should -Contain 'fast=true'
        $output | Should -Contain 'scripts=true'
        $output | Should -Contain 'workflows=true'
    }

    It 'runs everything for an empty or potentially truncated comparison' -ForEach @(
        @{ Count = 0 }, @{ Count = 300 }
    ) {
        $json = Join-Path $TestDrive 'incomplete.json'
        $files = @(for ($i = 0; $i -lt $Count; $i++) { @{ filename = "docs/$i.md"; patch = '+prose' } })
        ConvertTo-Json -InputObject $files | Set-Content -LiteralPath $json
        $output = & $script:Classifier -Files $json -MaxFiles 300
        $output | Should -Contain 'fast=true'
        $output | Should -Contain 'code=true'
        $output | Should -Contain 'scripts=true'
        $output | Should -Contain 'workflows=true'
    }

    It 'leaves build and desktop jobs off for a draft' {
        $json = Join-Path $TestDrive 'draft.json'
        ConvertTo-Json -InputObject @(@{ filename = '.github/workflows/ci.yml'; patch = '' }) | Set-Content -LiteralPath $json
        $output = & $script:Classifier -Files $json -Draft
        $output | Should -Contain 'fast=false'
        $output | Should -Contain 'code=false'
        $output | Should -Contain 'docs=false'
        $output | Should -Contain 'workflows=true'
    }
}
