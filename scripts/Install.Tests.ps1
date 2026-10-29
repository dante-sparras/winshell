#requires -Version 5.1
# Pester tests for scripts/install.ps1. They use a fake LOCALAPPDATA and never the real user PATH.

BeforeAll {
    . (Join-Path $PSScriptRoot 'Install.TestHelpers.ps1')
    . (Join-Path $PSScriptRoot 'install.ps1')
    $script:InstallFile = Join-Path $PSScriptRoot 'install.ps1'
    $script:UninstallFile = Join-Path $PSScriptRoot 'uninstall.ps1'
    $script:Utf8 = [Text.UTF8Encoding]::new($false)
    $script:OriginalEnv = @{
        LOCALAPPDATA             = $env:LOCALAPPDATA
        APPDATA                  = $env:APPDATA
        WINSHELL_VERSION         = $env:WINSHELL_VERSION
        WINSHELL_AUTOSTART       = $env:WINSHELL_AUTOSTART
        WINSHELL_RELEASE_BASE    = $env:WINSHELL_RELEASE_BASE
        WINSHELL_USER_PATH_FILE  = $env:WINSHELL_USER_PATH_FILE
        WINSHELL_AUTOSTART_FILE  = $env:WINSHELL_AUTOSTART_FILE
        WINSHELL_INSTALL_TEST    = $env:WINSHELL_INSTALL_TEST
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

Describe 'install.ps1' {
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

Describe 'argument and environment handling' {
    It 'treats an empty version as the latest release' {
        Resolve-WinshellVersion -Version '' | Should -BeNullOrEmpty
        Resolve-WinshellVersion | Should -BeNullOrEmpty
    }

    It 'reads WINSHELL_VERSION when -Version is empty' {
        $env:WINSHELL_VERSION = '0.6.0'
        Resolve-WinshellVersion -Version '  ' | Should -Be '0.6.0'
    }

    It 'prefers -Version over WINSHELL_VERSION' {
        $env:WINSHELL_VERSION = '9.9.9'
        Resolve-WinshellVersion -Version '0.6.0' | Should -Be '0.6.0'
    }

    It 'accepts a leading v' {
        Resolve-WinshellVersion -Version 'v1.2.3' | Should -Be '1.2.3'
        Resolve-WinshellVersion -Version 'V1.2.3' | Should -Be '1.2.3'
    }

    It 'rejects a version that is not x.y.z' {
        { Resolve-WinshellVersion -Version '1.2' } | Should -Throw '*x.y.z*'
        { Resolve-WinshellVersion -Version '../secret' } | Should -Throw '*x.y.z*'
        { Resolve-WinshellVersion -Version '0.6.0;calc' } | Should -Throw '*x.y.z*'
    }

    It 'turns sign-in on from the switch or from WINSHELL_AUTOSTART' {
        Resolve-WinshellAutoStart -Switch $true | Should -BeTrue
        $env:WINSHELL_AUTOSTART = '1'
        Resolve-WinshellAutoStart -Switch $false | Should -BeTrue
        $env:WINSHELL_AUTOSTART = 'true'
        Resolve-WinshellAutoStart -Switch $false | Should -BeTrue
        $env:WINSHELL_AUTOSTART = 'YES'
        Resolve-WinshellAutoStart -Switch $false | Should -BeTrue
    }

    It 'leaves sign-in off for 0 and lets the switch win' {
        $env:WINSHELL_AUTOSTART = '0'
        Resolve-WinshellAutoStart -Switch $false | Should -BeFalse
        Resolve-WinshellAutoStart -Switch $true | Should -BeTrue
        Remove-Item Env:WINSHELL_AUTOSTART
        Resolve-WinshellAutoStart -Switch $false | Should -BeFalse
    }
}

Describe 'architecture' {
    It 'maps x64 names' {
        Get-WinshellArchitecture -Name 'X64' | Should -Be 'x64'
        Get-WinshellArchitecture -Name 'AMD64' | Should -Be 'x64'
        Get-WinshellArchitecture -Name 'amd64' | Should -Be 'x64'
    }

    It 'maps arm64 names' {
        Get-WinshellArchitecture -Name 'Arm64' | Should -Be 'arm64'
        Get-WinshellArchitecture -Name 'ARM64' | Should -Be 'arm64'
    }

    It 'rejects an architecture Winshell does not publish' {
        { Get-WinshellArchitecture -Name 'X86' } | Should -Throw '*win-x64*'
        { Get-WinshellArchitecture -Name 'msil' } | Should -Throw '*win-x64*'
    }

    It 'detects this PC as x64 or arm64' {
        Get-WinshellArchitecture | Should -BeIn @('x64', 'arm64')
    }
}

Describe 'release URL' {
    It 'defaults to the GitHub releases URL' {
        Get-WinshellReleaseBase | Should -Be 'https://github.com/dante-sparras/winshell/releases'
    }

    It 'allows a loopback test server and rejects anything else' {
        $env:WINSHELL_RELEASE_BASE = 'http://127.0.0.1:9/releases'
        Get-WinshellReleaseBase | Should -Be 'http://127.0.0.1:9/releases'
        $env:WINSHELL_RELEASE_BASE = 'http://localhost:80/releases/'
        Get-WinshellReleaseBase | Should -Be 'http://localhost:80/releases'
        $env:WINSHELL_RELEASE_BASE = 'https://github.com/other/winshell/releases'
        { Get-WinshellReleaseBase } | Should -Throw '*WINSHELL_RELEASE_BASE*'
        $env:WINSHELL_RELEASE_BASE = 'http://127.0.0.1.evil/releases'
        { Get-WinshellReleaseBase } | Should -Throw '*WINSHELL_RELEASE_BASE*'
    }

    It 'builds the latest and versioned download URLs' {
        $base = 'https://github.com/dante-sparras/winshell/releases'
        Get-WinshellDownloadUrl $base $null 'SHA256SUMS.txt' | Should -Be "$base/latest/download/SHA256SUMS.txt"
        Get-WinshellDownloadUrl $base '0.6.0' 'winshell-0.6.0-win-x64.zip' |
            Should -Be "$base/download/v0.6.0/winshell-0.6.0-win-x64.zip"
        { Get-WinshellDownloadUrl $base '0.6.0' '../secret' } | Should -Throw '*file name*'
    }
}

Describe 'checksums' {
    It 'parses sha256sum lines' {
        $text = "AABBCCDD" + ('ee' * 28) + "  winshell-0.6.0-win-x64.zip`r`n" +
            ('ab' * 32) + " *winshell-0.6.0-win-arm64.zip`n"
        $map = ConvertFrom-WinshellChecksumMap $text
        $map['winshell-0.6.0-win-x64.zip'] | Should -Be (('aabbccdd' + ('ee' * 28)))
        $map.ContainsKey('winshell-0.6.0-win-arm64.zip') | Should -BeTrue
    }

    It 'rejects a line that is not a checksum and an empty file' {
        { ConvertFrom-WinshellChecksumMap "hello`n" } | Should -Throw '*not a checksum*'
        { ConvertFrom-WinshellChecksumMap "   `n" } | Should -Throw '*empty*'
    }

    It 'picks the newest zip for this architecture' {
        $map = @{
            'winshell-0.5.0-win-x64.zip'   = 'aa'
            'winshell-0.6.0-win-x64.zip'   = 'bb'
            'winshell-0.9.0-win-arm64.zip' = 'cc'
        }
        Select-WinshellPackageName $map 'x64' $null | Should -Be 'winshell-0.6.0-win-x64.zip'
        Select-WinshellPackageName $map 'x64' '0.5.0' | Should -Be 'winshell-0.5.0-win-x64.zip'
        { Select-WinshellPackageName $map 'x64' '1.0.0' } | Should -Throw '*no winshell-1.0.0-win-x64.zip*'
    }
}

Describe 'PATH entries' {
    It 'adds an entry once and removes it without touching the others' {
        Enter-WinshellInstallTest | Out-Null
        [IO.File]::WriteAllText($env:WINSHELL_USER_PATH_FILE, 'C:\Windows;C:\Tools', $script:Utf8)
        Add-WinshellPathEntry 'C:\Winshell\bin' | Should -BeTrue
        Add-WinshellPathEntry 'C:\Winshell\bin\' | Should -BeFalse
        Add-WinshellPathEntry 'c:\winshell\BIN' | Should -BeFalse
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools;C:\Winshell\bin'
        Remove-WinshellPathEntry 'C:\WINSHELL\bin\' | Should -BeTrue
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools'
        Remove-WinshellPathEntry 'C:\Winshell\bin' | Should -BeFalse
        Read-WinshellTestPath | Should -Be 'C:\Windows;C:\Tools'
    }
}

Describe 'quoting' {
    It 'quotes only arguments that need it' {
        Format-WinshellArgument 'winshell' | Should -Be 'winshell'
        Format-WinshellArgument 'C:\Program Files\winshell.exe' | Should -Be '"C:\Program Files\winshell.exe"'
        Format-WinshellArgument 'a"b' | Should -Be '"a\"b"'
    }
}

Describe 'install' {
    It 'refuses to touch the real PATH when a test forgets its path file' {
        $env:WINSHELL_INSTALL_TEST = '1'
        { Set-WinshellPathValue 'C:\should-not-write' } | Should -Throw '*WINSHELL_USER_PATH_FILE*'
    }

    It 'aborts a checksum mismatch before creating the install folder' {
        $ctx = Enter-WinshellInstallTest
        $zip = Join-Path $TestDrive 'good.zip'
        New-WinshellTestZip -Path $zip -Marker 'marker'
        $sums = ('11' * 32) + '  winshell-0.6.0-win-x64.zip'
        $arch = Get-WinshellArchitecture
        if ($arch -eq 'arm64') { $sums = ('11' * 32) + '  winshell-0.6.0-win-arm64.zip' }
        $downloadText = { param($Url) $sums }.GetNewClosure()
        $downloadFile = {
            param($Url, $Destination)
            Copy-Item -LiteralPath $zip -Destination $Destination
        }.GetNewClosure()
        { Install-Winshell -Version '0.6.0' -DownloadText $downloadText -DownloadFile $downloadFile } |
            Should -Throw '*SHA256 mismatch*'
        Test-Path -LiteralPath $ctx.Root | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'good.zip') | Should -BeTrue
    }

    It 'rejects an archive entry that escapes the folder' {
        Enter-WinshellInstallTest | Out-Null
        $zip = Join-Path $TestDrive 'slip.zip'
        New-WinshellTestZip -Path $zip -Marker 'x' -Names @() -ExtraEntries @('../outside.txt')
        $dest = Join-Path $TestDrive 'stage'
        { Expand-WinshellZip $zip $dest } | Should -Throw '*not inside the archive*'
        Test-Path -LiteralPath (Join-Path $TestDrive 'outside.txt') | Should -BeFalse
    }

    It 'installs into bin, records PATH once, and does not replace a running copy' {
        $ctx = Enter-WinshellInstallTest
        $env:LOCALAPPDATA = Join-Path $TestDrive 'User Name\AppData\Local'
        $env:APPDATA = Join-Path $TestDrive 'User Name\AppData\Roaming'
        New-Item -ItemType Directory -Force -Path $env:LOCALAPPDATA, $env:APPDATA | Out-Null
        $arch = Get-WinshellArchitecture
        $name = "winshell-0.6.0-win-$arch.zip"
        $zip = Join-Path $TestDrive $name
        New-WinshellTestZip -Path $zip -Marker 'marker-a'
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = "$hash  $name"
        $urls = [System.Collections.Generic.List[string]]::new()
        $downloadText = {
            param($Url)
            $urls.Add($Url)
            return $sums
        }.GetNewClosure()
        $downloadFile = {
            param($Url, $Destination)
            $urls.Add($Url)
            Copy-Item -LiteralPath $zip -Destination $Destination
        }.GetNewClosure()
        Install-Winshell -Version '0.6.0' -AutoStart -DownloadText $downloadText -DownloadFile $downloadFile
        $bin = Join-Path $env:LOCALAPPDATA 'Programs\winshell\bin'
        [IO.File]::ReadAllText((Join-Path $bin 'winshell.exe')) | Should -Be 'marker-a'
        Test-Path -LiteralPath (Join-Path $bin 'winshellw.exe') | Should -BeTrue
        $path = Read-WinshellTestPath
        @($path.Split(';') | Where-Object { $_ -eq $bin }).Count | Should -Be 1
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Winshell.lnk'))
        $link.TargetPath | Should -Be (Join-Path $bin 'winshellw.exe')
        $link.Arguments | Should -Be '--no-duplicate'
        $auto = [IO.File]::ReadAllText($env:WINSHELL_AUTOSTART_FILE, $script:Utf8)
        $auto | Should -Match 'conhost\.exe'
        $auto | Should -Match '--headless'
        $auto | Should -Match '--no-duplicate'
        $auto | Should -Match ([regex]::Escape((Join-Path $bin 'winshell.exe')))
        $urls | Should -Contain "https://github.com/dante-sparras/winshell/releases/download/v0.6.0/SHA256SUMS.txt"
        $urls | Should -Contain "https://github.com/dante-sparras/winshell/releases/download/v0.6.0/$name"
        Install-Winshell -Version '0.6.0' -DownloadText $downloadText -DownloadFile $downloadFile
        @((Read-WinshellTestPath).Split(';') | Where-Object { $_ -eq $bin }).Count | Should -Be 1
        { Install-Winshell -Version '0.6.0' -DownloadText $downloadText -DownloadFile $downloadFile -ProcessesUnder { 4242 } } |
            Should -Throw '*4242*'
        [IO.File]::ReadAllText((Join-Path $bin 'winshell.exe')) | Should -Be 'marker-a'
        $ctx | Out-Null
    }

    It 'follows -Version when a newer zip is also listed, and latest otherwise' {
        Enter-WinshellInstallTest | Out-Null
        $arch = Get-WinshellArchitecture
        $oldName = "winshell-0.5.0-win-$arch.zip"
        $newName = "winshell-0.6.0-win-$arch.zip"
        $oldZip = Join-Path $TestDrive $oldName
        $newZip = Join-Path $TestDrive $newName
        New-WinshellTestZip -Path $oldZip -Marker 'old'
        New-WinshellTestZip -Path $newZip -Marker 'new'
        $oldHash = (Get-FileHash -LiteralPath $oldZip -Algorithm SHA256).Hash.ToLowerInvariant()
        $newHash = (Get-FileHash -LiteralPath $newZip -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = "$oldHash  $oldName`n$newHash  $newName"
        $urls = [System.Collections.Generic.List[string]]::new()
        $downloadText = { param($Url) $urls.Add($Url); return $sums }.GetNewClosure()
        $downloadFile = {
            param($Url, $Destination)
            $urls.Add($Url)
            $leaf = [IO.Path]::GetFileName(($Url -split '/')[-1])
            $source = if ($leaf -eq $oldName) { $oldZip } else { $newZip }
            Copy-Item -LiteralPath $source -Destination $Destination
        }.GetNewClosure()
        Install-Winshell -DownloadText $downloadText -DownloadFile $downloadFile
        $bin = Join-Path $env:LOCALAPPDATA 'Programs\winshell\bin'
        [IO.File]::ReadAllText((Join-Path $bin 'winshell.exe')) | Should -Be 'new'
        @($urls | Where-Object { $_ -like '*/latest/download/*' }).Count | Should -BeGreaterThan 0
        Install-Winshell -Version '0.5.0' -DownloadText $downloadText -DownloadFile $downloadFile
        [IO.File]::ReadAllText((Join-Path $bin 'winshell.exe')) | Should -Be 'old'
        @($urls | Where-Object { $_ -like '*/download/v0.5.0/*' }).Count | Should -BeGreaterThan 0
    }

    It 'does not install an archive that has no winshell.exe' {
        $ctx = Enter-WinshellInstallTest
        $arch = Get-WinshellArchitecture
        $name = "winshell-0.6.0-win-$arch.zip"
        $zip = Join-Path $TestDrive 'empty.zip'
        New-WinshellTestZip -Path $zip -Marker 'x' -Names @('readme.txt')
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = "$hash  $name"
        $downloadText = { param($Url) $sums }.GetNewClosure()
        $downloadFile = {
            param($Url, $Destination)
            Copy-Item -LiteralPath $zip -Destination $Destination
        }.GetNewClosure()
        { Install-Winshell -Version '0.6.0' -DownloadText $downloadText -DownloadFile $downloadFile } |
            Should -Throw '*winshell.exe*'
        Test-Path -LiteralPath $ctx.Bin | Should -BeFalse
    }

    It 'refuses to install through a directory link' {
        $ctx = Enter-WinshellInstallTest
        $target = Join-Path $TestDrive 'junction-target'
        New-Item -ItemType Directory -Path $target | Out-Null
        [IO.File]::WriteAllText((Join-Path $target 'keep.txt'), 'keep', $script:Utf8)
        $programs = Split-Path -Parent $ctx.Root
        New-Item -ItemType Directory -Force -Path $programs | Out-Null
        if (Test-Path -LiteralPath $ctx.Root) { Remove-Item -LiteralPath $ctx.Root -Recurse -Force }
        New-Item -ItemType Junction -Path $ctx.Root -Target $target | Out-Null
        { Install-Winshell -Version '0.6.0' -DownloadText { 'unused' } -DownloadFile { throw 'downloaded' } } |
            Should -Throw '*link*'
        Test-Path -LiteralPath (Join-Path $target 'keep.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $target 'bin') | Should -BeFalse
    }
}

Describe 'script safety' {
    It 'does not invoke downloaded text or change execution policy, and forces TLS 1.2' {
        $names = Get-WinshellScriptCommandName $script:InstallFile
        $names | Should -Not -Contain 'Invoke-Expression'
        $names | Should -Not -Contain 'iex'
        $names | Should -Not -Contain 'Set-ExecutionPolicy'
        (Get-Content -LiteralPath $script:InstallFile -Raw) | Should -Match 'Tls12'
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:InstallFile, [ref]$tokens, [ref]$errors)
        $params = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
        $params | Should -Be @('Version', 'AutoStart')
    }
}

Describe 'end to end' {
    It 'installs from a local release, aborts on a bad checksum, and uninstalls' -Tag 'E2E' {
        $work = Join-Path $TestDrive 'e2e'
        $serve = Join-Path $work 'serve'
        $local = Join-Path $work 'Local'
        $roaming = Join-Path $work 'Roaming'
        New-Item -ItemType Directory -Path $serve, $local, $roaming | Out-Null
        $arch = Get-WinshellArchitecture
        $name = "winshell-0.6.0-win-$arch.zip"
        $other = if ($arch -eq 'x64') { 'arm64' } else { 'x64' }
        $zip = Join-Path $serve $name
        New-WinshellTestZip -Path $zip -Marker 'marker-a'
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $sumsPath = Join-Path $serve 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($sumsPath, "$hash  $name`n$('ab' * 32)  winshell-0.6.0-win-$other.zip", $script:Utf8)
        $server = Start-WinshellFakeRelease $serve
        try {
            $child = @{
                WINSHELL_INSTALL_TEST   = '1'
                LOCALAPPDATA            = $local
                APPDATA                 = $roaming
                WINSHELL_USER_PATH_FILE = Join-Path $work 'path.txt'
                WINSHELL_AUTOSTART_FILE = Join-Path $work 'autostart.txt'
                WINSHELL_RELEASE_BASE   = $server.Base
                WINSHELL_AUTOSTART      = '1'
            }
            $installed = Invoke-WinshellScriptFile -File $script:InstallFile -Environment $child
            $installed.ExitCode | Should -Be 0
            $installed.Output | Should -Match 'Verifying SHA256'
            $installed.Output | Should -Not -Match ([char]27)
            $exe = Join-Path $local 'Programs\winshell\bin\winshell.exe'
            [IO.File]::ReadAllText($exe) | Should -Be 'marker-a'
            $bin = Join-Path $local 'Programs\winshell\bin'
            @(([IO.File]::ReadAllText($child.WINSHELL_USER_PATH_FILE, $script:Utf8)).Split(';') | Where-Object { $_ -eq $bin }).Count | Should -Be 1
            Test-Path -LiteralPath (Join-Path $roaming 'Microsoft\Windows\Start Menu\Programs\Winshell.lnk') | Should -BeTrue
            [IO.File]::ReadAllText($child.WINSHELL_AUTOSTART_FILE, $script:Utf8) | Should -Match '--no-duplicate'
            [IO.File]::ReadAllText($server.Log) | Should -Match '/releases/latest/download/SHA256SUMS.txt'

            New-WinshellTestZip -Path $zip -Marker 'marker-b'
            $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::WriteAllText($sumsPath, "$hash  $name", $script:Utf8)
            $replaced = Invoke-WinshellScriptFile -File $script:InstallFile -Argument @('-Version', '0.6.0') -Environment @{
                WINSHELL_INSTALL_TEST   = '1'
                LOCALAPPDATA            = $local
                APPDATA                 = $roaming
                WINSHELL_USER_PATH_FILE = $child.WINSHELL_USER_PATH_FILE
                WINSHELL_AUTOSTART_FILE = $child.WINSHELL_AUTOSTART_FILE
                WINSHELL_RELEASE_BASE   = $server.Base
            }
            $replaced.ExitCode | Should -Be 0
            [IO.File]::ReadAllText($exe) | Should -Be 'marker-b'
            @(([IO.File]::ReadAllText($child.WINSHELL_USER_PATH_FILE, $script:Utf8)).Split(';') | Where-Object { $_ -eq $bin }).Count | Should -Be 1
            [IO.File]::ReadAllText($server.Log) | Should -Match '/releases/download/v0.6.0/'
            Test-Path -LiteralPath $child.WINSHELL_AUTOSTART_FILE | Should -BeTrue

            [IO.File]::WriteAllText($sumsPath, "$('cd' * 32)  $name", $script:Utf8)
            $bad = Invoke-WinshellScriptFile -File $script:InstallFile -Argument @('-Version', '0.6.0') -Environment @{
                WINSHELL_INSTALL_TEST   = '1'
                LOCALAPPDATA            = $local
                APPDATA                 = $roaming
                WINSHELL_USER_PATH_FILE = $child.WINSHELL_USER_PATH_FILE
                WINSHELL_AUTOSTART_FILE = $child.WINSHELL_AUTOSTART_FILE
                WINSHELL_RELEASE_BASE   = $server.Base
            }
            $bad.ExitCode | Should -Be 1
            $bad.Error | Should -Match 'SHA256 mismatch'
            [IO.File]::ReadAllText($exe) | Should -Be 'marker-b'

            $removed = Invoke-WinshellScriptFile -File $script:UninstallFile -Environment @{
                WINSHELL_INSTALL_TEST   = '1'
                LOCALAPPDATA            = $local
                APPDATA                 = $roaming
                WINSHELL_USER_PATH_FILE = $child.WINSHELL_USER_PATH_FILE
                WINSHELL_AUTOSTART_FILE = $child.WINSHELL_AUTOSTART_FILE
            }
            $removed.ExitCode | Should -Be 0
            $removed.Output | Should -Match 'uninstalled'
            Test-Path -LiteralPath (Join-Path $local 'Programs\winshell') | Should -BeFalse
            [IO.File]::ReadAllText($child.WINSHELL_USER_PATH_FILE, $script:Utf8) | Should -Not -Match [regex]::Escape($bin)
            Test-Path -LiteralPath $child.WINSHELL_AUTOSTART_FILE | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $roaming 'Microsoft\Windows\Start Menu\Programs\Winshell.lnk') | Should -BeFalse

            $again = Invoke-WinshellScriptFile -File $script:UninstallFile -Environment @{
                WINSHELL_INSTALL_TEST   = '1'
                LOCALAPPDATA            = $local
                APPDATA                 = $roaming
                WINSHELL_USER_PATH_FILE = $child.WINSHELL_USER_PATH_FILE
                WINSHELL_AUTOSTART_FILE = $child.WINSHELL_AUTOSTART_FILE
            }
            $again.ExitCode | Should -Be 0
            $again.Output | Should -Match 'not installed'
        } finally {
            Stop-WinshellFakeRelease $server
        }
    }
}
}
