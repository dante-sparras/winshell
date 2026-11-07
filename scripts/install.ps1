#requires -Version 5.1
<#
.SYNOPSIS
    Installs the portable Winshell build for the current user.

.DESCRIPTION
    No administrator rights. Downloads the x64 or arm64 zip from GitHub Releases, checks it against
    SHA256SUMS.txt, and stops without changing an existing install if the hash does not match.
    Installs into %LOCALAPPDATA%\Programs\winshell\bin, adds that folder to the user PATH, and creates
    a Start menu shortcut. Refuses to replace an install while that Winshell is running.
    irm | iex, and running this file with the call operator, also add that folder to this session's
    PATH and tell you to run winshell. pwsh -File leaves the caller unchanged and tells you to open
    a new terminal.
    If a Microsoft Store copy is also installed, it says so and still finishes.

    iex cannot pass parameters. WINSHELL_VERSION (x.y.z) and WINSHELL_AUTOSTART (1, true, or yes)
    are the same choices as -Version and -AutoStart.

        irm https://github.com/enpaze/winshell/releases/latest/download/install.ps1 | iex
        & ([scriptblock]::Create((irm https://github.com/enpaze/winshell/releases/latest/download/install.ps1))) -Version x.y.z -AutoStart

    This file is a release asset, fetched on its own. It does not dot-source a neighbor, and it does not
    run downloaded text. docs/development.md describes the variables its tests set.
#>
param(
    [string] $Version,
    [switch] $AutoStart
)

# uninstall.ps1 has the same copy of the helpers both scripts need, since each is fetched alone. Install.Tests.ps1 fails
# when the copies differ.
function Write-WinshellLine([string] $Message) { [Console]::Out.WriteLine($Message) }

function Assert-WinshellTestIsolation {
    if ($env:WINSHELL_INSTALL_TEST -ne '1') { return }
    if (-not $env:WINSHELL_USER_PATH_FILE -or -not $env:WINSHELL_AUTOSTART_FILE) {
        throw 'A test must set WINSHELL_USER_PATH_FILE and WINSHELL_AUTOSTART_FILE.'
    }
    $real = [Environment]::GetFolderPath('LocalApplicationData'), [Environment]::GetFolderPath('ApplicationData')
    foreach ($folder in $env:LOCALAPPDATA, $env:APPDATA) {
        if (-not $folder -or $real -contains [IO.Path]::GetFullPath($folder).TrimEnd('\')) {
            throw 'Refusing to use the real LOCALAPPDATA or APPDATA from a test.'
        }
    }
}

function Assert-WinshellNotRunning([string] $Root, [string] $Again) {
    $prefix = $Root.TrimEnd('\') + '\'
    # A process whose path Windows will not tell us could be this install.
    $ids = @(Get-CimInstance Win32_Process -Filter "Name = 'winshell.exe' OR Name = 'winshellw.exe'" |
            Where-Object { -not $_.ExecutablePath -or $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { $_.ProcessId })
    if ($ids) { throw "Winshell is running (pid $($ids -join ', ')). Close it, then run the $Again again.`nNothing was changed." }
}

function Remove-WinshellFolder([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if (-not $item) { return $false }
    # Windows PowerShell's Remove-Item -Recurse can follow a junction out of the folder.
    if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Refusing to delete $Path because it is a link." }
    Remove-Item -LiteralPath $Path -Recurse -Force
    return $true
}

function Get-WinshellUserPath {
    # Tests read and write a file instead of HKCU\Environment.
    if ($env:WINSHELL_USER_PATH_FILE) {
        if (Test-Path -LiteralPath $env:WINSHELL_USER_PATH_FILE) { return [IO.File]::ReadAllText($env:WINSHELL_USER_PATH_FILE) }
        return ''
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
    if (-not $key) { return '' }
    try { return [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames') } finally { $key.Dispose() }
}

function Set-WinshellUserPath([string] $Value) {
    if ($env:WINSHELL_USER_PATH_FILE) {
        [IO.File]::WriteAllText($env:WINSHELL_USER_PATH_FILE, $Value)
        return
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
    try {
        # REG_EXPAND_SZ stays, so an entry such as %USERPROFILE%\bin still expands.
        $kind = 'ExpandString'
        if ($key.GetValueNames() -contains 'Path' -and $key.GetValueKind('Path') -eq 'String') { $kind = 'String' }
        $key.SetValue('Path', $Value, $kind)
    } finally { $key.Dispose() }
    # WM_SETTINGCHANGE, so a terminal Explorer starts next sees the new PATH.
    if (-not ('Winshell.Install.Native' -as [type])) {
        Add-Type -Namespace Winshell.Install -Name Native -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);'
    }
    $result = [UIntPtr]::Zero
    [void][Winshell.Install.Native]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result)
}

function Receive-WinshellFile([string] $Uri, [string] $Path) {
    try {
        Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing
    } catch {
        throw "Could not download $Uri. $($_.Exception.Message)"
    }
}

# The command Winshell's SignInEntry.CommandFor writes for $Exe with no arguments, so Winshell reads this entry as its
# own. StartupRegistryTests runs this function and compares the two. It quotes only what has a space.
function Get-WinshellSignInCommand([string] $Exe) {
    @((Join-Path $env:SystemRoot 'System32\conhost.exe'), '--headless', $Exe, '--no-build', '--no-duplicate' |
            ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
}

function Install-Winshell([string] $Version, [switch] $AutoStart, [switch] $SessionKeepsPath) {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    Assert-WinshellTestIsolation
    $Version = "$Version".Trim()
    if (-not $Version) { $Version = "$env:WINSHELL_VERSION".Trim() }
    if ($Version) {
        $Version = $Version -replace '^v', ''
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be x.y.z, not '$Version'." }
    }
    $signIn = $AutoStart -or ("$env:WINSHELL_AUTOSTART".Trim() -match '^(1|true|yes)$')
    $arch = switch ([string][Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        'X64' { 'x64' }
        'Arm64' { 'arm64' }
        default { throw "Winshell publishes win-x64 and win-arm64. This PC is '$_'." }
    }
    $base = 'https://github.com/enpaze/winshell/releases'
    if ($env:WINSHELL_RELEASE_BASE) {
        # Tests serve a release from this PC. Nothing else stands in for GitHub.
        $base = $env:WINSHELL_RELEASE_BASE.Trim().TrimEnd('/')
        if ($base -ne 'https://github.com/enpaze/winshell/releases' -and
            $base -notmatch '^http://(127\.0\.0\.1|localhost)(:[0-9]{1,5})?(/[A-Za-z0-9._~/-]*)?$') {
            throw 'WINSHELL_RELEASE_BASE must be https://github.com/enpaze/winshell/releases or an http://127.0.0.1 test server.'
        }
    }
    $root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\winshell'))
    $bin = Join-Path $root 'bin'
    $stage = Join-Path $root 'bin.new'
    $old = Join-Path $root 'bin.old'
    Assert-WinshellNotRunning $root 'installer'
    if ($Version) {
        Write-WinshellLine "Installing Winshell $Version (win-$arch)."
    } else {
        Write-WinshellLine "Installing the latest Winshell (win-$arch)."
    }
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        # Windows PowerShell can default to TLS 1.0, which GitHub refuses.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    $release = if ($Version) { "$base/download/v$Version" } else { "$base/latest/download" }
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('winshell-install-' + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        Write-WinshellLine 'Downloading SHA256SUMS.txt.'
        Receive-WinshellFile "$release/SHA256SUMS.txt" (Join-Path $temp 'SHA256SUMS.txt')
        $name = if ($Version) { [regex]::Escape("winshell-$Version-win-$arch.zip") } else { "winshell-\d+\.\d+\.\d+-win-$arch\.zip" }
        $line = [regex]::Match([IO.File]::ReadAllText((Join-Path $temp 'SHA256SUMS.txt')), "(?m)^([0-9A-Fa-f]{64}) [ *]($name)\r?$")
        if (-not $line.Success) { throw "That release lists no win-$arch zip in SHA256SUMS.txt." }
        $package = $line.Groups[2].Value
        $installed = $package.Split('-')[1]
        # latest/download moves when a release is published, so the zip comes from the release its checksum names.
        Write-WinshellLine "Downloading $package."
        $zip = Join-Path $temp $package
        Receive-WinshellFile "$base/download/v$installed/$package" $zip
        Write-WinshellLine 'Verifying SHA256.'
        $expected = $line.Groups[1].Value.ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "SHA256 mismatch for $package.`nExpected: $expected`nActual:   $actual`nThe download was discarded. Nothing was installed."
        }
        # The stage is next to bin, on the same volume, so the old copy goes aside and the new one in by renaming.
        # A file still open in bin fails the first rename, before anything changed.
        Remove-WinshellFolder $stage | Out-Null
        Remove-WinshellFolder $old | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
        foreach ($required in 'winshell.exe', 'winshellw.exe') {
            if (-not (Test-Path -LiteralPath (Join-Path $stage $required))) { throw "The archive does not contain $required. Nothing was installed." }
        }
        Write-WinshellLine "Installing to $bin."
        if (Test-Path -LiteralPath $bin) { Rename-Item -LiteralPath $bin -NewName 'bin.old' }
        Rename-Item -LiteralPath $stage -NewName 'bin'
    } finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $stage) { Remove-WinshellFolder $stage | Out-Null }
    }
    try {
        Remove-WinshellFolder $old | Out-Null
    } catch {
        Write-WinshellLine "The previous copy is still at $old. $($_.Exception.Message)"
    }

    $parts = @((Get-WinshellUserPath).Split(';') | Where-Object { $_ })
    if ($parts | Where-Object { $_.TrimEnd('\') -eq $bin }) {
        Write-WinshellLine "$bin is already on your PATH."
    } else {
        Write-WinshellLine "Adding $bin to your PATH."
        Set-WinshellUserPath (($parts + $bin) -join ';')
    }
    # This session's PATH is separate from the stored one. Tests leave it alone.
    if (-not $env:WINSHELL_USER_PATH_FILE -and -not ("$env:Path".Split(';') | Where-Object { $_.TrimEnd('\') -eq $bin })) {
        $env:Path = "$env:Path".TrimEnd(';') + ';' + $bin
    }

    Write-WinshellLine 'Creating the Start menu shortcut.'
    $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Item -ItemType Directory -Force -Path $programs | Out-Null
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $programs 'Winshell.lnk'))
    $link.TargetPath = Join-Path $bin 'winshellw.exe'
    $link.Arguments = '--no-duplicate'
    $link.WorkingDirectory = $bin
    $link.Description = 'Winshell'
    $link.Save()

    if ($signIn) {
        $command = Get-WinshellSignInCommand (Join-Path $bin 'winshell.exe')
        if ($env:WINSHELL_AUTOSTART_FILE) {
            [IO.File]::WriteAllText($env:WINSHELL_AUTOSTART_FILE, $command)
        } else {
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
            try { $key.SetValue('Winshell', $command) } finally { $key.Dispose() }
        }
        Write-WinshellLine 'Winshell will run when you sign in.'
    }

    if ($SessionKeepsPath) {
        Write-WinshellLine "Winshell $installed is installed. Run winshell."
    } else {
        Write-WinshellLine "Winshell $installed is installed. Open a new terminal and run winshell."
    }
    # A test must not ask this PC which packages it has.
    $store = @()
    if ($env:WINSHELL_INSTALL_TEST -ne '1') {
        try { $store = @(Get-AppxPackage -Name '*Winshell*') } catch { $store = @() }
    }
    if ($store.Count -gt 0) {
        Write-WinshellLine 'A Microsoft Store copy of Winshell is also installed. Its WindowsApps alias usually comes first on PATH, so winshell may start the Store app instead of this copy.'
    }
}

# WDAC and AppLocker can hold a session in Constrained Language Mode, where the .NET calls above fail.
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
    Write-Error -ErrorAction Continue "install.ps1 needs PowerShell's full language mode, and a system policy such as WDAC or AppLocker keeps this session in $($ExecutionContext.SessionState.LanguageMode). Download the portable zip from https://github.com/enpaze/winshell/releases instead."
    if ($PSCommandPath) { exit 1 }
} else {
    try {
        # iex has no script path, and `& .\install.ps1` returns to this session, so either keeps the PATH just added.
        $keeps = -not $PSCommandPath -or $MyInvocation.InvocationName -eq '&'
        Install-Winshell -Version $Version -AutoStart:$AutoStart -SessionKeepsPath:$keeps
    } catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        # exit would close the window iex runs in.
        if ($PSCommandPath) { exit 1 }
    }
}
