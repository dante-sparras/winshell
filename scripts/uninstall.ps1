#requires -Version 5.1
<#
.SYNOPSIS
    Removes the per-user Winshell install created by install.ps1.

.DESCRIPTION
    Removes %LOCALAPPDATA%\Programs\winshell, that bin folder's user PATH entry, the Winshell Start
    menu shortcut, and the Winshell sign-in entry when it starts that install. It does not remove
    configuration, cache, or logs under %APPDATA%\winshell and %LOCALAPPDATA%\winshell.

    Stops without changing anything while that Winshell is running. Safe to run when Winshell is
    already gone.

        irm https://github.com/dante-sparras/winshell/releases/latest/download/uninstall.ps1 | iex

    This file is a release asset. It does not dot-source a neighbor, and it does not run downloaded text.
#>

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

function Get-WinshellImagePath([int] $ProcessId) {
    # MainModule.FileName is empty from a 32-bit host and does not throw. Limited query works across that boundary.
    if (-not ('Winshell.Install.Process' -as [type])) {
        Add-Type -Namespace Winshell.Install -Name Process -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
public static extern System.IntPtr OpenProcess(uint access, bool inherit, int processId);
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern bool QueryFullProcessImageNameW(System.IntPtr process, int flags, System.Text.StringBuilder name, ref int size);
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
public static extern bool CloseHandle(System.IntPtr handle);
'@
    }
    $handle = [Winshell.Install.Process]::OpenProcess(0x1000, $false, $ProcessId)
    if ($handle -eq [IntPtr]::Zero) { return $null }
    try {
        $capacity = 32768
        $builder = New-Object System.Text.StringBuilder $capacity
        $size = $capacity
        $ok = [Winshell.Install.Process]::QueryFullProcessImageNameW($handle, 0, $builder, [ref]$size)
        if (-not $ok -or $builder.Length -eq 0) { return $null }
        return $builder.ToString()
    } finally {
        [void][Winshell.Install.Process]::CloseHandle($handle)
    }
}

function Get-WinshellProcessIdUnder([string] $Directory) {
    $root = [IO.Path]::GetFullPath($Directory)
    if (-not $root.EndsWith('\')) { $root = $root + '\' }
    $ids = @()
    foreach ($name in @('winshell', 'winshellw')) {
        $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        foreach ($proc in $procs) {
            $path = Get-WinshellImagePath ([int]$proc.Id)
            if ([string]::IsNullOrWhiteSpace($path)) {
                throw "Winshell is running (pid $($proc.Id)). Close it, then run the uninstaller again.`nNothing was changed."
            }
            if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
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

function Remove-WinshellPathEntry([string] $Entry) {
    $parts = @(Split-WinshellPath (Get-WinshellPathValue))
    $kept = @($parts | Where-Object { -not (Test-WinshellSamePath $_ $Entry) })
    if ($kept.Count -eq $parts.Count) { return $false }
    Set-WinshellPathValue ($kept -join ';')
    return $true
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

function Test-WinshellSameFile([string] $Left, [string] $Right) {
    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) { return $false }
    return [string]::Equals(
        [IO.Path]::GetFullPath($Left).TrimEnd('\'),
        [IO.Path]::GetFullPath($Right).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)
}

function Test-WinshellCommandTarget([string] $Command, [string] $Exe) {
    if ([string]::IsNullOrWhiteSpace($Command) -or [string]::IsNullOrWhiteSpace($Exe)) { return $false }
    $full = [IO.Path]::GetFullPath($Exe)
    $pattern = [regex]::Escape($full)
    return [regex]::IsMatch($Command, "(?i)(^|[\s""])$pattern($|[\s""])")
}

function Get-WinshellShortcutTarget([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    return [string]$link.TargetPath
}

function Uninstall-Winshell {
    [CmdletBinding()]
    param([scriptblock] $ProcessesUnder)
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version 2.0
    Confirm-WinshellTestIsolation
    $root = Get-WinshellInstallRoot
    $bin = Get-WinshellBin $root
    $exe = Join-Path $bin 'winshell.exe'
    $gui = Join-Path $bin 'winshellw.exe'
    if (Test-Path -LiteralPath $root) {
        if (Test-WinshellReparsePoint $root) {
            throw "Refusing to remove $root because it is a link.`nNothing was changed."
        }
        $running = @(Get-WinshellRunningId $root $ProcessesUnder)
        if ($running.Count -gt 0) {
            throw "Winshell is running (pid $($running -join ', ')). Close it, then run the uninstaller again.`nNothing was changed."
        }
    }
    $removed = $false
    if (Test-Path -LiteralPath $root) {
        Write-WinshellLine "Removing $root."
        Remove-Item -LiteralPath $root -Recurse -Force
        $removed = $true
    }
    if (Remove-WinshellPathEntry $bin) {
        Write-WinshellLine "Removing $bin from your PATH."
        $removed = $true
    }
    $shortcut = Get-WinshellShortcutPath
    $target = Get-WinshellShortcutTarget $shortcut
    if ((Test-WinshellSameFile $target $exe) -or (Test-WinshellSameFile $target $gui)) {
        Write-WinshellLine 'Removing the Start menu shortcut.'
        Remove-Item -LiteralPath $shortcut -Force
        $removed = $true
    }
    $command = Get-WinshellAutostartCommand
    if (Test-WinshellCommandTarget $command $exe) {
        Write-WinshellLine 'Removing the sign-in entry.'
        Set-WinshellAutostartCommand ''
        $removed = $true
    }
    if (-not $removed) {
        Write-WinshellLine 'Winshell is not installed.'
        return
    }
    Write-WinshellLine 'Winshell is uninstalled. Your configuration in %APPDATA%\winshell was left in place.'
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Uninstall-Winshell
    } catch {
        $message = $_.Exception.Message
        if ([string]::IsNullOrWhiteSpace($message)) { $message = $_.ToString() }
        [Console]::Error.WriteLine($message)
        # iex leaves PSCommandPath empty. exit would close that session.
        if ([string]::IsNullOrEmpty($PSCommandPath)) { return }
        exit 1
    }
}
