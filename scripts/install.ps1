#requires -Version 5.1
<#
.SYNOPSIS
    Installs the portable Winshell build for the current user.

.DESCRIPTION
    No administrator rights. Downloads the x64 or arm64 zip from GitHub Releases, checks it against
    SHA256SUMS.txt, and stops without changing an existing install if the hash does not match.
    Installs into %LOCALAPPDATA%\Programs\winshell\bin, adds that folder to the user PATH, and creates
    a Start menu shortcut. Refuses to replace an install while that Winshell is running.

    The installed files are the portable build, so Winshell's own update check keeps working.

    iex cannot pass parameters. WINSHELL_VERSION (x.y.z) and WINSHELL_AUTOSTART (1, true, or yes)
    are the same choices as -Version and -AutoStart.

        irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
        & ([scriptblock]::Create((irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1))) -Version 0.6.0 -AutoStart

    This file is a release asset. It does not dot-source a neighbor, and it does not run downloaded text.
#>
param(
    [string] $Version,
    [switch] $AutoStart
)

function Write-WinshellLine([string] $Message) {
    [Console]::Out.WriteLine($Message)
}

function Confirm-WinshellTestIsolation {
    if ($env:WINSHELL_INSTALL_TEST -ne '1') { return }
    if ([string]::IsNullOrWhiteSpace($env:WINSHELL_USER_PATH_FILE)) {
        throw 'Refusing to change PATH: a test must set WINSHELL_USER_PATH_FILE.'
    }
    if ([string]::IsNullOrWhiteSpace($env:WINSHELL_AUTOSTART_FILE)) {
        throw 'Refusing to change sign-in: a test must set WINSHELL_AUTOSTART_FILE.'
    }
    $realLocal = [Environment]::GetFolderPath('LocalApplicationData')
    $realRoaming = [Environment]::GetFolderPath('ApplicationData')
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or
        [string]::Equals([IO.Path]::GetFullPath($env:LOCALAPPDATA), [IO.Path]::GetFullPath($realLocal), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to use the real LOCALAPPDATA from a test.'
    }
    if ([string]::IsNullOrWhiteSpace($env:APPDATA) -or
        [string]::Equals([IO.Path]::GetFullPath($env:APPDATA), [IO.Path]::GetFullPath($realRoaming), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to use the real APPDATA from a test.'
    }
}

function Get-WinshellInstallRoot {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { throw 'LOCALAPPDATA is not set.' }
    $root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\winshell'))
    $suffix = [IO.Path]::DirectorySeparatorChar + 'Programs' + [IO.Path]::DirectorySeparatorChar + 'winshell'
    if (-not $root.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use $root. Winshell installs into %LOCALAPPDATA%\Programs\winshell."
    }
    return $root
}

function Get-WinshellBin([string] $Root) {
    return (Join-Path $Root 'bin')
}

function Get-WinshellStartMenuFolder {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { throw 'APPDATA is not set.' }
    return [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))
}

function Get-WinshellShortcutPath {
    return (Join-Path (Get-WinshellStartMenuFolder) 'Winshell.lnk')
}

function Test-WinshellReparsePoint([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    return [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
}

function Get-WinshellArchitecture {
    param([string] $Name)
    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = $null
        try { $Name = [string][Runtime.InteropServices.RuntimeInformation]::OSArchitecture } catch { $Name = $null }
        if ([string]::IsNullOrWhiteSpace($Name)) {
            if (-not [string]::IsNullOrWhiteSpace($env:PROCESSOR_ARCHITEW6432)) {
                $Name = $env:PROCESSOR_ARCHITEW6432
            } else {
                $Name = $env:PROCESSOR_ARCHITECTURE
            }
        }
    }
    switch -Regex ($Name) {
        '^(X64|AMD64)$' { return 'x64' }
        '^(Arm64|ARM64)$' { return 'arm64' }
        default { throw "Winshell publishes win-x64 and win-arm64. This PC is '$Name'." }
    }
}

function Resolve-WinshellVersion {
    param([string] $Version)
    $value = $Version
    if ([string]::IsNullOrWhiteSpace($value)) { $value = $env:WINSHELL_VERSION }
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    $value = $value.Trim()
    if ($value.StartsWith('v') -or $value.StartsWith('V')) { $value = $value.Substring(1) }
    if ($value -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be x.y.z, not '$value'." }
    return $Matches[0]
}

function Resolve-WinshellAutoStart {
    param([bool] $Switch)
    if ($Switch) { return $true }
    $value = $env:WINSHELL_AUTOSTART
    if ([string]::IsNullOrWhiteSpace($value)) { return $false }
    return $value.Trim() -match '^(1|true|yes)$'
}

function Get-WinshellReleaseBase {
    $official = 'https://github.com/dante-sparras/winshell/releases'
    $base = $official
    if (-not [string]::IsNullOrWhiteSpace($env:WINSHELL_RELEASE_BASE)) {
        $base = $env:WINSHELL_RELEASE_BASE.Trim().TrimEnd('/')
    }
    if ($base -eq $official) { return $base }
    if ($base -match '^http://(127\.0\.0\.1|localhost)(:[0-9]{1,5})?(/[A-Za-z0-9._~/-]*)?$') { return $base }
    throw 'WINSHELL_RELEASE_BASE must be https://github.com/dante-sparras/winshell/releases or an http://127.0.0.1 test server.'
}

function Get-WinshellDownloadUrl([string] $Base, [string] $Version, [string] $File) {
    if ($File -notmatch '^[A-Za-z0-9._-]+$') { throw "Unexpected release file name '$File'." }
    if ([string]::IsNullOrWhiteSpace($Version)) { return "$Base/latest/download/$File" }
    return "$Base/download/v$Version/$File"
}

function ConvertFrom-WinshellChecksumMap([string] $Text) {
    $map = @{}
    $clean = $Text.TrimStart([char]0xFEFF)
    foreach ($line in @($clean -split '\r?\n')) {
        if ($line.Trim().Length -eq 0) { continue }
        if ($line -notmatch '^([0-9A-Fa-f]{64}) [ *](\S+)$') {
            throw 'SHA256SUMS.txt has a line that is not a checksum.'
        }
        $map[$Matches[2]] = $Matches[1].ToLowerInvariant()
    }
    if ($map.Count -eq 0) { throw 'SHA256SUMS.txt is empty.' }
    return $map
}

function Select-WinshellPackageName($Checksums, [string] $Arch, [string] $Version) {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $name = "winshell-$Version-win-$Arch.zip"
        if (-not $Checksums.ContainsKey($name)) { throw "Release v$Version has no $name in SHA256SUMS.txt." }
        return $name
    }
    $best = $null
    $bestVersion = $null
    foreach ($key in @($Checksums.Keys)) {
        if ($key -match "^winshell-(\d+\.\d+\.\d+)-win-$Arch\.zip$") {
            $parsed = [version]$Matches[1]
            if ($null -eq $bestVersion -or $parsed -gt $bestVersion) {
                $bestVersion = $parsed
                $best = $key
            }
        }
    }
    if (-not $best) { throw "The latest release has no win-$Arch zip in SHA256SUMS.txt." }
    return $best
}

function Add-WinshellCompressionAssembly {
    if (-not ('System.IO.Compression.ZipFile' -as [type])) {
        Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    }
    if (-not ('System.IO.Compression.ZipFile' -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }
}

function Expand-WinshellZip([string] $ZipPath, [string] $Destination) {
    Add-WinshellCompressionAssembly
    $destRoot = [IO.Path]::GetFullPath($Destination)
    if (-not (Test-Path -LiteralPath $destRoot)) {
        New-Item -ItemType Directory -Path $destRoot | Out-Null
    }
    $prefix = $destRoot.TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.EndsWith('/')) { continue }
            $segments = $name.Split('/')
            foreach ($segment in $segments) {
                if ($segment -eq '' -or $segment -eq '.' -or $segment -eq '..' -or $segment.IndexOf(':') -ge 0) {
                    throw "The archive entry '$($entry.FullName)' is not inside the archive."
                }
            }
            $relative = $name.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $target = [IO.Path]::GetFullPath((Join-Path $destRoot $relative))
            if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "The archive entry '$($entry.FullName)' escapes the install folder."
            }
            $parent = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $parent)) {
                New-Item -ItemType Directory -Path $parent | Out-Null
            }
            $inputStream = $entry.Open()
            try {
                $output = [IO.File]::Create($target)
                try { $inputStream.CopyTo($output) } finally { $output.Dispose() }
            } finally { $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
}

function Receive-WinshellWeb([string] $Uri, [string] $OutFile) {
    $previousProtocol = $null
    $previousProgress = $ProgressPreference
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    }
    $ProgressPreference = 'SilentlyContinue'
    try {
        $params = @{
            Uri             = $Uri
            UseBasicParsing = $true
            Headers         = @{ 'User-Agent' = 'winshell-install' }
        }
        if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
            $params.OutFile = $OutFile
            Invoke-WebRequest @params | Out-Null
            return
        }
        $content = (Invoke-WebRequest @params).Content
        if ($content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($content) }
        return [string]$content
    } catch {
        throw "Could not download $Uri. $($_.Exception.Message)"
    } finally {
        $ProgressPreference = $previousProgress
        if ($null -ne $previousProtocol) {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
        }
    }
}

function Get-WinshellProcessIdUnder([string] $Directory) {
    $root = [IO.Path]::GetFullPath($Directory)
    if (-not $root.EndsWith('\')) { $root = $root + '\' }
    $ids = @()
    foreach ($name in @('winshell', 'winshellw')) {
        $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        foreach ($proc in $procs) {
            $path = $null
            try { $path = $proc.MainModule.FileName } catch { $path = $null }
            if ($path -and $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                $ids += [int]$proc.Id
            }
        }
    }
    return $ids
}

function Get-WinshellRunningId([string] $Directory, [scriptblock] $ProcessesUnder) {
    $ids = if ($ProcessesUnder) { @(& $ProcessesUnder $Directory) } else { @(Get-WinshellProcessIdUnder $Directory) }
    return @($ids | Where-Object { $null -ne $_ -and "$_" -ne '' })
}

function Split-WinshellPath([string] $Path) {
    if ([string]::IsNullOrEmpty($Path)) { return @() }
    return @($Path.Split(';') | Where-Object { $_ -ne '' })
}

function Test-WinshellSamePath([string] $Left, [string] $Right) {
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    return [string]::Equals($Left.TrimEnd('\', '/'), $Right.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)
}

function Get-WinshellPathValue {
    Confirm-WinshellTestIsolation
    if (-not [string]::IsNullOrWhiteSpace($env:WINSHELL_USER_PATH_FILE)) {
        if (-not (Test-Path -LiteralPath $env:WINSHELL_USER_PATH_FILE)) { return '' }
        return [IO.File]::ReadAllText($env:WINSHELL_USER_PATH_FILE, [Text.UTF8Encoding]::new($false))
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $false)
    if (-not $key) { return '' }
    try {
        $raw = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($null -eq $raw) { return '' }
        return [string]$raw
    } finally { $key.Dispose() }
}

function Publish-WinshellEnvironmentChange {
    if (-not [string]::IsNullOrWhiteSpace($env:WINSHELL_USER_PATH_FILE)) { return }
    if (-not ('Winshell.Install.Native' -as [type])) {
        Add-Type -Namespace Winshell.Install -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, int msg, System.UIntPtr wParam, string lParam, uint flags, uint timeout, out System.UIntPtr result);
'@
    }
    $result = [UIntPtr]::Zero
    [void][Winshell.Install.Native]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result)
}

function Set-WinshellPathValue([string] $Value) {
    Confirm-WinshellTestIsolation
    if (-not [string]::IsNullOrWhiteSpace($env:WINSHELL_USER_PATH_FILE)) {
        $parent = Split-Path -Parent $env:WINSHELL_USER_PATH_FILE
        if ($parent -and -not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent | Out-Null
        }
        [IO.File]::WriteAllText($env:WINSHELL_USER_PATH_FILE, $Value, [Text.UTF8Encoding]::new($false))
        return
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    if (-not $key) { $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment') }
    try {
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        try { $kind = $key.GetValueKind('Path') } catch { $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString }
        if ($kind -ne [Microsoft.Win32.RegistryValueKind]::ExpandString -and $kind -ne [Microsoft.Win32.RegistryValueKind]::String) {
            $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        }
        $key.SetValue('Path', $Value, $kind)
    } finally { $key.Dispose() }
    Publish-WinshellEnvironmentChange
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { -not [string]::IsNullOrEmpty($_) }) -join ';'
}

function Add-WinshellPathEntry([string] $Entry) {
    $entry = $Entry.TrimEnd('\')
    $parts = @(Split-WinshellPath (Get-WinshellPathValue))
    foreach ($part in $parts) {
        if (Test-WinshellSamePath $part $entry) { return $false }
    }
    $updated = @($parts + $entry)
    Set-WinshellPathValue ($updated -join ';')
    return $true
}

function Remove-WinshellPathEntry([string] $Entry) {
    $parts = @(Split-WinshellPath (Get-WinshellPathValue))
    $kept = @($parts | Where-Object { -not (Test-WinshellSamePath $_ $Entry) })
    if ($kept.Count -eq $parts.Count) { return $false }
    Set-WinshellPathValue ($kept -join ';')
    return $true
}

function Format-WinshellArgument([string] $Argument) {
    $special = [char[]]@(32, 9, 34)
    if ($Argument.Length -gt 0 -and $Argument.IndexOfAny($special) -lt 0) { return $Argument }
    $sb = [Text.StringBuilder]::new('"')
    $slashes = 0
    foreach ($c in $Argument.ToCharArray()) {
        if ($c -eq '\') {
            $slashes++
            continue
        }
        if ($c -eq '"') {
            [void]$sb.Append([char]92, ($slashes * 2) + 1)
        } elseif ($slashes -gt 0) {
            [void]$sb.Append([char]92, $slashes)
        }
        $slashes = 0
        [void]$sb.Append($c)
    }
    if ($slashes -gt 0) { [void]$sb.Append([char]92, $slashes * 2) }
    [void]$sb.Append('"')
    return $sb.ToString()
}

function Get-WinshellSignInCommand([string] $Exe) {
    $conhost = Join-Path $env:SystemRoot 'System32\conhost.exe'
    return (@(
            (Format-WinshellArgument $conhost),
            '--headless',
            (Format-WinshellArgument $Exe),
            '--no-duplicate'
        ) -join ' ')
}

function Get-WinshellAutostartCommand {
    Confirm-WinshellTestIsolation
    if (-not [string]::IsNullOrWhiteSpace($env:WINSHELL_AUTOSTART_FILE)) {
        if (-not (Test-Path -LiteralPath $env:WINSHELL_AUTOSTART_FILE)) { return $null }
        $text = [IO.File]::ReadAllText($env:WINSHELL_AUTOSTART_FILE, [Text.UTF8Encoding]::new($false))
        if ($text.Length -eq 0) { return $null }
        return $text
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $false)
    if (-not $key) { return $null }
    try {
        $value = $key.GetValue('Winshell', $null)
        if ($value -is [string] -and $value.Length -gt 0) { return $value }
        return $null
    } finally { $key.Dispose() }
}

function Set-WinshellAutostartCommand([string] $Command) {
    Confirm-WinshellTestIsolation
    if (-not [string]::IsNullOrWhiteSpace($env:WINSHELL_AUTOSTART_FILE)) {
        if ([string]::IsNullOrEmpty($Command)) {
            if (Test-Path -LiteralPath $env:WINSHELL_AUTOSTART_FILE) {
                Remove-Item -LiteralPath $env:WINSHELL_AUTOSTART_FILE -Force
            }
            return
        }
        $parent = Split-Path -Parent $env:WINSHELL_AUTOSTART_FILE
        if ($parent -and -not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent | Out-Null
        }
        [IO.File]::WriteAllText($env:WINSHELL_AUTOSTART_FILE, $Command, [Text.UTF8Encoding]::new($false))
        return
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
    if (-not $key) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    }
    try {
        if ([string]::IsNullOrEmpty($Command)) {
            $key.DeleteValue('Winshell', $false)
        } else {
            $key.SetValue('Winshell', $Command, [Microsoft.Win32.RegistryValueKind]::String)
        }
    } finally { $key.Dispose() }
}

function Set-WinshellShortcut([string] $Exe) {
    Confirm-WinshellTestIsolation
    $path = Get-WinshellShortcutPath
    $folder = Split-Path -Parent $path
    if (Test-WinshellReparsePoint $folder) { throw "Refusing to create a shortcut in $folder because it is a link." }
    if (-not (Test-Path -LiteralPath $folder)) {
        New-Item -ItemType Directory -Path $folder | Out-Null
    }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $Exe
    $link.Arguments = '--no-duplicate'
    $link.WorkingDirectory = Split-Path -Parent $Exe
    $link.Description = 'Winshell'
    $link.Save()
}

function Move-WinshellDirectory([string] $From, [string] $To) {
    try {
        [IO.Directory]::Move($From, $To)
    } catch [System.IO.IOException] {
        Copy-Item -LiteralPath $From -Destination $To -Recurse -Force
        Remove-Item -LiteralPath $From -Recurse -Force
    }
}

function Install-WinshellBin([string] $Stage, [string] $Bin, [scriptblock] $ProcessesUnder) {
    $parent = Split-Path -Parent $Bin
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    if (Test-WinshellReparsePoint $parent) { throw "Refusing to install into $parent because it is a link." }
    if (Test-WinshellReparsePoint $Bin) { throw "Refusing to replace $Bin because it is a link." }
    $running = @(Get-WinshellRunningId $parent $ProcessesUnder)
    if ($running.Count -gt 0) {
        throw "Winshell is running (pid $($running -join ', ')). Close it, then run the installer again.`nNothing was changed."
    }
    $old = Join-Path $parent ('bin.old-' + [guid]::NewGuid().ToString('n'))
    $movedOld = $false
    if (Test-Path -LiteralPath $Bin) {
        [IO.Directory]::Move($Bin, $old)
        $movedOld = $true
    }
    try {
        Move-WinshellDirectory $Stage $Bin
    } catch {
        if (Test-Path -LiteralPath $Bin) {
            Remove-Item -LiteralPath $Bin -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($movedOld -and -not (Test-Path -LiteralPath $Bin)) {
            [IO.Directory]::Move($old, $Bin)
        }
        throw
    }
    if ($movedOld -and (Test-Path -LiteralPath $old)) {
        try {
            Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction Stop
        } catch {
            Write-WinshellLine "Winshell is installed. The previous copy is still at $old because it could not be deleted."
        }
    }
}

function Install-Winshell {
    [CmdletBinding()]
    param(
        [string] $Version,
        [switch] $AutoStart,
        [scriptblock] $DownloadText,
        [scriptblock] $DownloadFile,
        [scriptblock] $ProcessesUnder
    )
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version 2.0
    Confirm-WinshellTestIsolation
    $resolved = Resolve-WinshellVersion -Version $Version
    $enableSignIn = Resolve-WinshellAutoStart -Switch ([bool]$AutoStart)
    $arch = Get-WinshellArchitecture
    $root = Get-WinshellInstallRoot
    $bin = Get-WinshellBin $root
    if (Test-WinshellReparsePoint $root) { throw "Refusing to install into $root because it is a link." }
    if (Test-Path -LiteralPath $root) {
        $running = @(Get-WinshellRunningId $root $ProcessesUnder)
        if ($running.Count -gt 0) {
            throw "Winshell is running (pid $($running -join ', ')). Close it, then run the installer again.`nNothing was changed."
        }
    }
    if ($resolved) {
        Write-WinshellLine "Installing Winshell $resolved (win-$arch)."
    } else {
        Write-WinshellLine "Installing the latest Winshell (win-$arch)."
    }
    $base = Get-WinshellReleaseBase
    $sumsUrl = Get-WinshellDownloadUrl $base $resolved 'SHA256SUMS.txt'
    Write-WinshellLine 'Downloading SHA256SUMS.txt.'
    $sumsText = if ($DownloadText) { [string](& $DownloadText $sumsUrl) } else { [string](Receive-WinshellWeb $sumsUrl $null) }
    $checksums = ConvertFrom-WinshellChecksumMap $sumsText
    $package = Select-WinshellPackageName $checksums $arch $resolved
    $expected = $checksums[$package]
    $packageUrl = Get-WinshellDownloadUrl $base $resolved $package
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('winshell-install-' + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $zip = Join-Path $temp 'download.zip'
        Write-WinshellLine "Downloading $package."
        if ($DownloadFile) { & $DownloadFile $packageUrl $zip } else { Receive-WinshellWeb $packageUrl $zip }
        if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) { throw "The download did not write $package." }
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-WinshellLine 'Verifying SHA256.'
        if ($actual -ne $expected) {
            throw "SHA256 mismatch for $package.`nExpected: $expected`nActual:   $actual`nThe download was discarded. Nothing was installed."
        }
        $stage = Join-Path $temp 'stage'
        Expand-WinshellZip $zip $stage
        foreach ($required in @('winshell.exe', 'winshellw.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) {
                throw "The archive does not contain $required. Nothing was installed."
            }
        }
        Write-WinshellLine "Installing to $bin."
        Install-WinshellBin $stage $bin $ProcessesUnder
    } finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if (Add-WinshellPathEntry $bin) {
        Write-WinshellLine "Adding $bin to your PATH."
    } else {
        Write-WinshellLine "$bin is already on your PATH."
    }
    Write-WinshellLine 'Creating the Start menu shortcut.'
    Set-WinshellShortcut (Join-Path $bin 'winshellw.exe')
    if ($enableSignIn) {
        Set-WinshellAutostartCommand (Get-WinshellSignInCommand (Join-Path $bin 'winshell.exe'))
        Write-WinshellLine 'Winshell will run when you sign in.'
    }
    $installed = $resolved
    if ($package -match '^winshell-(\d+\.\d+\.\d+)-') { $installed = $Matches[1] }
    Write-WinshellLine "Winshell $installed is installed. Open a new terminal and run winshell."
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Install-Winshell -Version $Version -AutoStart:$AutoStart
    } catch {
        $message = $_.Exception.Message
        if ([string]::IsNullOrWhiteSpace($message)) { $message = $_.ToString() }
        [Console]::Error.WriteLine($message)
        exit 1
    }
}
