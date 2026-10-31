#requires -Version 5.1
# Pester tests for scripts/uninstall.ps1. They use a fake LOCALAPPDATA and never the real user PATH.

BeforeAll {
    . (Join-Path $PSScriptRoot 'Install.TestHelpers.ps1')
    . (Join-Path $PSScriptRoot 'uninstall.ps1')
    $script:UninstallFile = Join-Path $PSScriptRoot 'uninstall.ps1'
    $script:Utf8 = [Text.UTF8Encoding]::new($false)
    $script:OriginalEnv = @{
        LOCALAPPDATA            = $env:LOCALAPPDATA
        APPDATA                 = $env:APPDATA
        WINSHELL_VERSION        = $env:WINSHELL_VERSION
        WINSHELL_AUTOSTART      = $env:WINSHELL_AUTOSTART
        WINSHELL_RELEASE_BASE   = $env:WINSHELL_RELEASE_BASE
        WINSHELL_USER_PATH_FILE = $env:WINSHELL_USER_PATH_FILE
        WINSHELL_AUTOSTART_FILE = $env:WINSHELL_AUTOSTART_FILE
        WINSHELL_INSTALL_TEST   = $env:WINSHELL_INSTALL_TEST
    }

    function script:Restore-WinshellTestEnv {
        foreach ($name in $script:OriginalEnv.Keys) {
            $value = $script:OriginalEnv[$name]
            if ($null -eq $value) {
                Remove-Item "Env:$name" -ErrorAction SilentlyContinue
            } else {
                Set-Item "Env:$name" $value
            }
        }
    }
}

Describe 'uninstall.ps1' {
BeforeEach {
    Restore-WinshellTestEnv
    foreach ($name in @(
            'WINSHELL_VERSION', 'WINSHELL_AUTOSTART', 'WINSHELL_RELEASE_BASE',
            'WINSHELL_USER_PATH_FILE', 'WINSHELL_AUTOSTART_FILE', 'WINSHELL_INSTALL_TEST')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    $script:ProfileBefore = Get-WinshellRealProfileSnapshot
}

AfterEach {
    try {
        Assert-WinshellRealProfileUnchanged $script:ProfileBefore
    } finally {
        Restore-WinshellTestEnv
    }
}

Describe 'uninstall' {
    It 'refuses to touch the real sign-in entry when a test forgets its file' {
        $env:WINSHELL_INSTALL_TEST = '1'
        $env:WINSHELL_USER_PATH_FILE = Join-Path $TestDrive 'user-path.txt'
        { Set-WinshellAutostartCommand '' } | Should -Throw '*WINSHELL_AUTOSTART_FILE*'
    }

    It 'removes only this install, and a second run is a no-op' {
        $ctx = Enter-WinshellInstallTest
        New-Item -ItemType Directory -Path $ctx.Bin | Out-Null
        [IO.File]::WriteAllText((Join-Path $ctx.Bin 'winshell.exe'), 'exe', $script:Utf8)
        [IO.File]::WriteAllText((Join-Path $ctx.Bin 'winshellw.exe'), 'gui', $script:Utf8)
        $config = Join-Path $ctx.Local 'winshell'
        New-Item -ItemType Directory -Path $config | Out-Null
        [IO.File]::WriteAllText((Join-Path $config 'preferences.json'), '{}', $script:Utf8)
        [IO.File]::WriteAllText($ctx.PathFile, "C:\Windows;$($ctx.Bin);C:\Tools", $script:Utf8)
        [IO.File]::WriteAllText($ctx.AutostartFile, "C:\Windows\System32\conhost.exe --headless `"$($ctx.Bin)\winshell.exe`" --no-duplicate", $script:Utf8)
        $shell = New-Object -ComObject WScript.Shell
        $folder = Split-Path -Parent $ctx.Shortcut
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $link = $shell.CreateShortcut($ctx.Shortcut)
        $link.TargetPath = Join-Path $ctx.Bin 'winshellw.exe'
        $link.Save()
        Uninstall-Winshell
        Test-Path -LiteralPath $ctx.Root | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $config 'preferences.json') | Should -BeTrue
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools'
        Test-Path -LiteralPath $ctx.AutostartFile | Should -BeFalse
        Test-Path -LiteralPath $ctx.Shortcut | Should -BeFalse
        Uninstall-Winshell
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools'
        Test-Path -LiteralPath (Join-Path $config 'preferences.json') | Should -BeTrue
    }

    It 'removes a sign-in entry that starts winshellw.exe in this install' {
        $ctx = Enter-WinshellInstallTest
        New-Item -ItemType Directory -Path $ctx.Bin | Out-Null
        [IO.File]::WriteAllText((Join-Path $ctx.Bin 'winshell.exe'), 'exe', $script:Utf8)
        [IO.File]::WriteAllText((Join-Path $ctx.Bin 'winshellw.exe'), 'gui', $script:Utf8)
        [IO.File]::WriteAllText($ctx.AutostartFile, "C:\Windows\System32\conhost.exe --headless `"$($ctx.Bin)\winshellw.exe`" --no-duplicate", $script:Utf8)
        Uninstall-Winshell
        Test-Path -LiteralPath $ctx.Root | Should -BeFalse
        Test-Path -LiteralPath $ctx.AutostartFile | Should -BeFalse
    }

    It 'leaves a sign-in entry and a shortcut that point somewhere else' {
        $ctx = Enter-WinshellInstallTest
        New-Item -ItemType Directory -Path $ctx.Bin | Out-Null
        [IO.File]::WriteAllText((Join-Path $ctx.Bin 'winshell.exe'), 'exe', $script:Utf8)
        [IO.File]::WriteAllText($ctx.PathFile, $ctx.Bin, $script:Utf8)
        [IO.File]::WriteAllText($ctx.AutostartFile, 'C:\Other\winshell.exe --no-duplicate', $script:Utf8)
        $shell = New-Object -ComObject WScript.Shell
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ctx.Shortcut) | Out-Null
        $link = $shell.CreateShortcut($ctx.Shortcut)
        $link.TargetPath = Join-Path $env:SystemRoot 'System32\notepad.exe'
        $link.Save()
        Uninstall-Winshell
        Test-Path -LiteralPath $ctx.Root | Should -BeFalse
        [IO.File]::ReadAllText($ctx.AutostartFile, $script:Utf8) | Should -Be 'C:\Other\winshell.exe --no-duplicate'
        Test-Path -LiteralPath $ctx.Shortcut | Should -BeTrue
        Read-WinshellTestPath | Should -Be ''
    }

    It 'removes a PATH entry once and keeps the others' {
        Enter-WinshellInstallTest | Out-Null
        [IO.File]::WriteAllText($env:WINSHELL_USER_PATH_FILE, 'C:\Windows;C:\Winshell\bin\;C:\Tools', $script:Utf8)
        Remove-WinshellPathEntry 'c:\winshell\bin' | Should -BeTrue
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools'
        Remove-WinshellPathEntry 'C:\Winshell\bin' | Should -BeFalse
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools'
    }

    It 'removes nothing while Winshell is running from the install folder' {
        $ctx = Enter-WinshellInstallTest
        New-Item -ItemType Directory -Path $ctx.Bin | Out-Null
        [IO.File]::WriteAllText((Join-Path $ctx.Bin 'winshell.exe'), 'exe', $script:Utf8)
        [IO.File]::WriteAllText($ctx.PathFile, "C:\Windows;$($ctx.Bin)", $script:Utf8)
        { Uninstall-Winshell -ProcessesUnder { 5151 } } | Should -Throw '*5151*'
        Test-Path -LiteralPath (Join-Path $ctx.Bin 'winshell.exe') | Should -BeTrue
        Read-WinshellTestPath | Should -Be "C:\Windows;$($ctx.Bin)"
    }

    It 'refuses to remove a directory link' {
        $ctx = Enter-WinshellInstallTest
        $target = Join-Path $TestDrive 'junction-target'
        New-Item -ItemType Directory -Path $target | Out-Null
        [IO.File]::WriteAllText((Join-Path $target 'keep.txt'), 'keep', $script:Utf8)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ctx.Root) | Out-Null
        if (Test-Path -LiteralPath $ctx.Root) { Remove-Item -LiteralPath $ctx.Root -Recurse -Force }
        New-Item -ItemType Junction -Path $ctx.Root -Target $target | Out-Null
        [IO.File]::WriteAllText($ctx.PathFile, 'C:\Windows', $script:Utf8)
        { Uninstall-Winshell } | Should -Throw '*link*'
        Test-Path -LiteralPath (Join-Path $target 'keep.txt') | Should -BeTrue
        Read-WinshellTestPath | Should -Be 'C:\Windows'
    }
}

Describe 'script safety' {
    It 'does not invoke downloaded text or change execution policy' {
        $names = Get-WinshellScriptCommandName $script:UninstallFile
        $names | Should -Not -Contain 'Invoke-Expression'
        $names | Should -Not -Contain 'iex'
        $names | Should -Not -Contain 'Set-ExecutionPolicy'
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:UninstallFile, [ref]$tokens, [ref]$errors)
        $ast.ParamBlock | Should -BeNullOrEmpty
    }
}

Describe 'unreadable process path' {
    It 'reads this process image path' {
        Get-WinshellImagePath $PID | Should -Be (Get-Process -Id $PID).MainModule.FileName
    }

    It 'refuses to delete an install when a Winshell process has no readable path' {
        $ctx = Enter-WinshellInstallTest
        New-Item -ItemType Directory -Path $ctx.Bin | Out-Null
        $marker = Join-Path $ctx.Bin 'keep.txt'
        [IO.File]::WriteAllText($marker, 'keep', $script:Utf8)
        [IO.File]::WriteAllText($ctx.PathFile, 'C:\Windows', $script:Utf8)
        Mock Get-Process {
            if ($Name -eq 'winshell') { return [pscustomobject]@{ Id = 2147483647 } }
            return @()
        }
        { Uninstall-Winshell } | Should -Throw '*running*'
        [IO.File]::ReadAllText($marker) | Should -Be 'keep'
        Read-WinshellTestPath | Should -Be 'C:\Windows'
    }
}

Describe 'failed invocation' {
    It 'returns from a failed script block and exits 1 when started as a file' {
        $quoted = $script:UninstallFile.Replace("'", "''")
        $inline = "`$env:WINSHELL_INSTALL_TEST = '1'; & ([scriptblock]::Create([IO.File]::ReadAllText('$quoted'))); Write-Output 'STILL-HERE'; exit 0"
        $block = Invoke-WinshellScriptFile -Command $inline
        $block.ExitCode | Should -Be 0
        $block.Output | Should -Match 'STILL-HERE'
        $block.Error | Should -Match 'WINSHELL_USER_PATH_FILE'

        $file = Invoke-WinshellScriptFile -File $script:UninstallFile -Environment @{ WINSHELL_INSTALL_TEST = '1' }
        $file.ExitCode | Should -Be 1
        $file.Error | Should -Match 'WINSHELL_USER_PATH_FILE'
    }

    It 'says what to do instead under Constrained Language Mode' {
        $quoted = $script:UninstallFile.Replace("'", "''")
        # WINSHELL_INSTALL_TEST with no fake profile makes the script refuse if the check ever stops it running.
        $inline = "`$env:WINSHELL_INSTALL_TEST = '1'; `$ExecutionContext.SessionState.LanguageMode = 'ConstrainedLanguage'; & '$quoted'; exit `$LASTEXITCODE"
        $result = Invoke-WinshellScriptFile -Command $inline
        $result.ExitCode | Should -Be 1
        $result.Error | Should -Match 'full language mode'
        $result.Error | Should -Match 'Delete'
        $result.Error | Should -Not -Match 'Cannot invoke method'
    }
}
}
