BeforeAll {
    . (Join-Path $PSScriptRoot '../../scripts/lib/Release.ps1')
}

Describe 'The files a release uploads' {
    It 'include every file the README and the installer scripts download from a release' {
        $text = foreach ($file in 'README.md', 'scripts/install.ps1', 'scripts/uninstall.ps1') {
            Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "../../$file")
        }
        $downloaded = [regex]::Matches(($text -join "`n"), '(?:latest/download|\$release)/([\w.-]+\.(?:ps1|txt))') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

        $downloaded | Should -Not -BeNullOrEmpty
        foreach ($name in $downloaded) { Get-ReleaseAssetName '1.2.3' | Should -Contain $name }
    }
}

Describe 'Finding a Windows SDK tool' {
    BeforeEach {
        $script:ProgramFiles = ${env:ProgramFiles(x86)}
        ${env:ProgramFiles(x86)} = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $script:Kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    }

    AfterEach { ${env:ProgramFiles(x86)} = $script:ProgramFiles }

    It 'takes it from the newest SDK that has it, by version and not by name' {
        foreach ($sdk in '10.0.9.0', '10.0.22621.0', '10.0.26100.0') {
            New-Item -ItemType Directory -Force -Path (Join-Path $script:Kits "$sdk/x64") | Out-Null
        }
        foreach ($sdk in '10.0.9.0', '10.0.22621.0') { Set-Content -LiteralPath (Join-Path $script:Kits "$sdk/x64/tool.exe") -Value '' }

        Get-WindowsSdkTool tool.exe | Should -Be (Join-Path $script:Kits '10.0.22621.0/x64/tool.exe')
    }

    It 'says to install the SDK when none has it' {
        { Get-WindowsSdkTool tool.exe } | Should -Throw '*Install the Windows SDK*'
    }
}

Describe 'Checking the version winshell.exe reports' {
    It 'accepts <Reported> for 1.2.3' -ForEach @(
        @{ Reported = 'winshell 1.2.3' }
        @{ Reported = 'winshell 1.2.3+abc123' }
    ) {
        $exe = Join-Path $TestDrive 'winshell.cmd'
        Set-Content -LiteralPath $exe -Value "@echo $Reported"
        Assert-WinshellVersion $exe '1.2.3' | Should -Be $Reported
    }

    It 'stops at <Reported> exiting <Exit> for 1.2.3' -ForEach @(
        @{ Reported = 'winshell 1.2.30'; Exit = 0 }
        @{ Reported = 'winshell 1.2.3'; Exit = 1 }
    ) {
        $exe = Join-Path $TestDrive 'winshell.cmd'
        Set-Content -LiteralPath $exe -Value "@echo $Reported`r`n@exit /b $Exit"
        { Assert-WinshellVersion $exe '1.2.3' } | Should -Throw '*not version 1.2.3*'
    }
}
