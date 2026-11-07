#requires -Version 5.1
# Pester tests for scripts/install.ps1 and scripts/uninstall.ps1. Each test runs a script in a new PowerShell of the
# same kind as this one, against releases served from this PC, with a profile of its own. WINSHELL_INSTALL_TEST makes
# the scripts refuse the real profile, and the PATH and sign-in files stand in for HKCU.

Describe 'install.ps1 and uninstall.ps1' {
    BeforeAll {
        $script:Install = Join-Path $PSScriptRoot '../../scripts/install.ps1'
        $script:Uninstall = Join-Path $PSScriptRoot '../../scripts/uninstall.ps1'
        $script:Arch = if ([string][Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
        $script:Served = Join-Path $TestDrive 'served'
        . (Join-Path $PSScriptRoot '../../scripts/lib/Release.ps1')
        Add-Type -AssemblyName System.IO.Compression.FileSystem

        for ($attempt = 1; -not $script:Listener; $attempt++) {
            $tcp = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
            $tcp.Start()
            $port = $tcp.LocalEndpoint.Port
            $tcp.Stop()
            $listener = [Net.HttpListener]::new()
            $listener.Prefixes.Add("http://127.0.0.1:$port/")
            try { $listener.Start(); $script:Listener = $listener } catch { $listener.Close(); if ($attempt -ge 5) { throw } }
        }
        $script:Base = "http://127.0.0.1:$port/releases"
        $script:Server = [powershell]::Create().AddScript({
                param($Listener, $Root)
                while ($Listener.IsListening) {
                    try { $context = $Listener.GetContext() } catch { break }
                    $file = Join-Path $Root $context.Request.Url.LocalPath
                    if (Test-Path -LiteralPath $file -PathType Leaf) {
                        $bytes = [IO.File]::ReadAllBytes($file)
                        $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                    } else {
                        $context.Response.StatusCode = 404
                    }
                    $context.Response.Close()
                }
            }).AddArgument($script:Listener).AddArgument($script:Served)
        $script:Serving = $script:Server.BeginInvoke()

        function Publish-Release([string] $Version, [switch] $Latest, [switch] $WrongHash) {
            $folder = Join-Path $script:Served "releases\download\v$Version"
            New-Item -ItemType Directory -Force -Path $folder | Out-Null
            $name = Get-ReleaseZipName $Version "win-$script:Arch"
            $zip = [IO.Compression.ZipFile]::Open((Join-Path $folder $name), 'Create')
            try {
                foreach ($exe in 'winshell.exe', 'winshellw.exe') {
                    $writer = [IO.StreamWriter]::new($zip.CreateEntry($exe).Open())
                    try { $writer.Write($Version) } finally { $writer.Dispose() }
                }
            } finally { $zip.Dispose() }
            $hash = (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($WrongHash) { $hash = 'ab' * 32 }
            [IO.File]::WriteAllText((Join-Path $folder 'SHA256SUMS.txt'), "$hash  $name`n")
            if ($Latest) {
                $moving = Join-Path $script:Served 'releases\latest\download'
                New-Item -ItemType Directory -Force -Path $moving | Out-Null
                Copy-Item -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Destination $moving
            }
        }

        # The folder has a space, so the sign-in command quotes the exe the way Winshell's own Settings does.
        function New-TestProfile {
            $root = Join-Path $TestDrive ('User ' + [guid]::NewGuid().ToString('n'))
            $p = [pscustomobject]@{
                Local     = Join-Path $root 'Local'
                Roaming   = Join-Path $root 'Roaming'
                PathFile  = Join-Path $root 'path.txt'
                SignIn    = Join-Path $root 'sign-in.txt'
                Root      = Join-Path $root 'Local\Programs\winshell'
                Bin       = Join-Path $root 'Local\Programs\winshell\bin'
                Shortcut  = Join-Path $root 'Roaming\Microsoft\Windows\Start Menu\Programs\Winshell.lnk'
            }
            New-Item -ItemType Directory -Path $p.Local, $p.Roaming | Out-Null
            [IO.File]::WriteAllText($p.PathFile, 'C:\Windows;C:\Tools')
            return $p
        }

        function Invoke-Script($TestProfile, [string[]] $File, [string] $Command, [hashtable] $Environment = @{}) {
            $psi = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $run = if ($Command) {
                '-EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
            } else {
                '-File ' + (($File | ForEach-Object { '"' + $_ + '"' }) -join ' ')
            }
            $psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass $run"
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            foreach ($name in @(@($psi.Environment.Keys) -like 'WINSHELL_*')) { [void]$psi.Environment.Remove($name) }
            $psi.Environment['WINSHELL_INSTALL_TEST'] = '1'
            $psi.Environment['WINSHELL_RELEASE_BASE'] = $script:Base
            $psi.Environment['WINSHELL_USER_PATH_FILE'] = $TestProfile.PathFile
            $psi.Environment['WINSHELL_AUTOSTART_FILE'] = $TestProfile.SignIn
            $psi.Environment['LOCALAPPDATA'] = $TestProfile.Local
            $psi.Environment['APPDATA'] = $TestProfile.Roaming
            foreach ($name in $Environment.Keys) { $psi.Environment[$name] = $Environment[$name] }
            $process = [Diagnostics.Process]::Start($psi)
            $output = $process.StandardOutput.ReadToEndAsync()
            $errors = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(120000)) {
                $process.Kill()
                throw "$File$Command did not finish."
            }
            $process.WaitForExit()
            return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output.Result; Error = $errors.Result }
        }

        function Get-Text([string] $Path) { [IO.File]::ReadAllText($Path) }

        Publish-Release '0.4.0' -WrongHash
        Publish-Release '0.5.0'
        Publish-Release '0.6.0' -Latest
    }

    AfterAll {
        $script:Listener.Close()
        $script:Server.EndInvoke($script:Serving)
        $script:Server.Dispose()
    }

    It 'define each helper they share the same way, since each is fetched alone' {
        $functions = foreach ($file in $Install, $Uninstall) {
            $tree = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path $file).Path, [ref]$null, [ref]$null)
            $all = $tree.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)
            $byName = @{}
            foreach ($function in $all) { $byName[$function.Name] = $function.Extent.Text }
            , $byName
        }
        $installs, $uninstalls = $functions
        $shared = @($uninstalls.Keys | Where-Object { $_ -ne 'Uninstall-Winshell' })
        $shared | Should -Not -BeNullOrEmpty
        foreach ($name in $shared) { $installs[$name] | Should -BeExactly $uninstalls[$name] -Because $name }
    }

    It 'installs the latest release, with sign-in from WINSHELL_AUTOSTART' {
        $p = New-TestProfile
        $result = Invoke-Script $p -File $Install -Environment @{ WINSHELL_AUTOSTART = 'yes' }
        $result.Error | Should -BeNullOrEmpty
        $result.ExitCode | Should -Be 0
        $result.Output | Should -Match 'Verifying SHA256'
        Get-Text (Join-Path $p.Bin 'winshellw.exe') | Should -Be '0.6.0'
        Get-Text $p.PathFile | Should -Be "C:\Windows;C:\Tools;$($p.Bin)"
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut($p.Shortcut)
        $link.TargetPath | Should -Be (Join-Path $p.Bin 'winshellw.exe')
        $link.Arguments | Should -Be '--no-duplicate'
        Get-Text $p.SignIn | Should -Be "$env:SystemRoot\System32\conhost.exe --headless `"$($p.Bin)\winshell.exe`" --no-build --no-duplicate"
        $result.Output | Should -Match 'Winshell 0\.6\.0 is installed\. Open a new terminal and run winshell\.'
    }

    It 'reinstalls a pinned version over the latest, given with a leading v' {
        $p = New-TestProfile
        (Invoke-Script $p -File $Install).ExitCode | Should -Be 0
        $result = Invoke-Script $p -File $Install, '-Version', 'v0.5.0'
        $result.ExitCode | Should -Be 0
        $result.Output | Should -Match "$([regex]::Escape($p.Bin)) is already on your PATH"
        Get-Text (Join-Path $p.Bin 'winshell.exe') | Should -Be '0.5.0'
        Get-Text $p.PathFile | Should -Be "C:\Windows;C:\Tools;$($p.Bin)"
        Get-ChildItem -LiteralPath $p.Root -Name | Should -Be 'bin'
        Test-Path -LiteralPath $p.SignIn | Should -BeFalse
    }

    It 'leaves the install alone when the zip does not match its checksum' {
        $p = New-TestProfile
        (Invoke-Script $p -File $Install).ExitCode | Should -Be 0
        $result = Invoke-Script $p -File $Install, '-Version', '0.4.0'
        $result.ExitCode | Should -Be 1
        $result.Error | Should -Match 'SHA256 mismatch for winshell-0\.4\.0'
        Get-Text (Join-Path $p.Bin 'winshell.exe') | Should -Be '0.6.0'
        Get-ChildItem -LiteralPath $p.Root -Name | Should -Be 'bin'
    }

    It 'changes nothing while Winshell runs from the install' {
        $p = New-TestProfile
        (Invoke-Script $p -File $Install).ExitCode | Should -Be 0
        # Any console program named winshell.exe in bin is a running Winshell to the scripts.
        Copy-Item -LiteralPath "$env:SystemRoot\System32\PING.EXE" -Destination (Join-Path $p.Bin 'winshell.exe') -Force
        $psi = [Diagnostics.ProcessStartInfo]::new((Join-Path $p.Bin 'winshell.exe'), '-n 600 127.0.0.1')
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $running = [Diagnostics.Process]::Start($psi)
        try {
            $install = Invoke-Script $p -File $Install, '-Version', '0.5.0'
            $install.ExitCode | Should -Be 1
            $install.Error | Should -Match "Winshell is running \(pid [^)]*\b$($running.Id)\b"
            $uninstall = Invoke-Script $p -File $Uninstall
            $uninstall.ExitCode | Should -Be 1
            $uninstall.Error | Should -Match 'Nothing was changed'
        } finally {
            $running.Kill()
            $running.WaitForExit()
        }
        Get-Text (Join-Path $p.Bin 'winshellw.exe') | Should -Be '0.6.0'
        Get-Text $p.PathFile | Should -Be "C:\Windows;C:\Tools;$($p.Bin)"
    }

    It 'returns to the iex session on failure, and tells it to run winshell on success' {
        $p = New-TestProfile
        $iex = "Get-Content -Raw -LiteralPath '$Install' | Invoke-Expression"
        $command = "`$env:WINSHELL_VERSION = 'not-a-version'; $iex; 'still here'; `$env:WINSHELL_VERSION = '0.5.0'; $iex"
        $result = Invoke-Script $p -Command $command
        $result.ExitCode | Should -Be 0
        $result.Error | Should -Match "Version must be x\.y\.z, not 'not-a-version'"
        $result.Output | Should -Match 'still here'
        $result.Output | Should -Match 'Winshell 0\.5\.0 is installed\. Run winshell\.'
        Get-Text (Join-Path $p.Bin 'winshell.exe') | Should -Be '0.5.0'
    }

    It 'uninstalls what it installed, keeps configuration and logs, and does nothing the second time' {
        $p = New-TestProfile
        (Invoke-Script $p -File $Install, '-AutoStart').ExitCode | Should -Be 0
        $config = Join-Path $p.Roaming 'winshell\config.toml'
        $log = Join-Path $p.Local 'winshell\logs\winshell.log'
        New-Item -ItemType Directory -Path (Split-Path $config), (Split-Path $log) | Out-Null
        [IO.File]::WriteAllText($config, 'config')
        [IO.File]::WriteAllText($log, 'log')
        $result = Invoke-Script $p -File $Uninstall
        $result.ExitCode | Should -Be 0
        $result.Output | Should -Match 'Winshell is uninstalled'
        Test-Path -LiteralPath $p.Root | Should -BeFalse
        Get-Text $p.PathFile | Should -Be 'C:\Windows;C:\Tools'
        Test-Path -LiteralPath $p.Shortcut | Should -BeFalse
        Test-Path -LiteralPath $p.SignIn | Should -BeFalse
        Get-Text $config | Should -Be 'config'
        Get-Text $log | Should -Be 'log'
        $again = Invoke-Script $p -File $Uninstall
        $again.ExitCode | Should -Be 0
        $again.Output | Should -Match 'Winshell is not installed\.'
    }

    It 'leaves the shortcut and sign-in entry of another install' {
        $p = New-TestProfile
        (Invoke-Script $p -File $Install).ExitCode | Should -Be 0
        $other = Join-Path $TestDrive 'Other\winshellw.exe'
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut($p.Shortcut)
        $link.TargetPath = $other
        $link.Save()
        [IO.File]::WriteAllText($p.SignIn, "C:\Windows\System32\conhost.exe --headless $other --no-duplicate")
        (Invoke-Script $p -File $Uninstall).ExitCode | Should -Be 0
        Test-Path -LiteralPath $p.Root | Should -BeFalse
        (New-Object -ComObject WScript.Shell).CreateShortcut($p.Shortcut).TargetPath | Should -Be $other
        Get-Text $p.SignIn | Should -Be "C:\Windows\System32\conhost.exe --headless $other --no-duplicate"
    }
}
