# Helpers for scripts/Install.Tests.ps1 and scripts/Uninstall.Tests.ps1.
# Not a release asset. Tests never point these at the real user profile.

function Get-WinshellRealProfileSnapshot {
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $roaming = [Environment]::GetFolderPath('ApplicationData')
    $path = ''
    $pathKind = ''
    $envKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $false)
    if ($envKey) {
        try {
            $raw = $envKey.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($null -ne $raw) { $path = [string]$raw }
            try { $pathKind = [string]$envKey.GetValueKind('Path') } catch { $pathKind = '' }
        } finally { $envKey.Dispose() }
    }
    $auto = ''
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $false)
    if ($runKey) {
        try {
            $value = $runKey.GetValue('Winshell', $null)
            if ($null -ne $value) { $auto = [string]$value }
        } finally { $runKey.Dispose() }
    }
    $install = Join-Path $local 'Programs\winshell'
    $lnk = Join-Path $roaming 'Microsoft\Windows\Start Menu\Programs\Winshell.lnk'
    return [pscustomobject]@{
        Path          = $path
        PathKind      = $pathKind
        Autostart     = $auto
        InstallWrite  = if (Test-Path -LiteralPath $install) { (Get-Item -LiteralPath $install).LastWriteTimeUtc.Ticks } else { -1 }
        ShortcutWrite = if (Test-Path -LiteralPath $lnk) { (Get-Item -LiteralPath $lnk).LastWriteTimeUtc.Ticks } else { -1 }
    }
}

function Restore-WinshellRealUserPath($Before) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    if (-not $key) { return }
    try {
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        if ($Before.PathKind -eq 'String') { $kind = [Microsoft.Win32.RegistryValueKind]::String }
        if ($Before.PathKind -eq 'ExpandString') { $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString }
        $key.SetValue('Path', [string]$Before.Path, $kind)
    } finally { $key.Dispose() }
}

function Restore-WinshellRealAutostart($Before) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
    if (-not $key) { return }
    try {
        if ([string]::IsNullOrEmpty($Before.Autostart)) {
            $key.DeleteValue('Winshell', $false)
        } else {
            $key.SetValue('Winshell', $Before.Autostart, [Microsoft.Win32.RegistryValueKind]::String)
        }
    } finally { $key.Dispose() }
}

function Assert-WinshellRealProfileUnchanged($Before) {
    $after = Get-WinshellRealProfileSnapshot
    if ($Before.Path -ne $after.Path -or $Before.PathKind -ne $after.PathKind) {
        Restore-WinshellRealUserPath $Before
        throw 'A test changed the real user PATH.'
    }
    if ($Before.Autostart -ne $after.Autostart) {
        Restore-WinshellRealAutostart $Before
        throw 'A test changed the real Winshell sign-in entry.'
    }
    if ($Before.InstallWrite -ne $after.InstallWrite -or $Before.ShortcutWrite -ne $after.ShortcutWrite) {
        throw 'A test changed the real Winshell install folder or Start menu shortcut.'
    }
}

function Enter-WinshellInstallTest {
    $root = Join-Path $TestDrive ('profile-' + [guid]::NewGuid().ToString('n'))
    $local = Join-Path $root 'Local'
    $roaming = Join-Path $root 'Roaming'
    New-Item -ItemType Directory -Force -Path $local, $roaming | Out-Null
    $env:WINSHELL_INSTALL_TEST = '1'
    $env:LOCALAPPDATA = $local
    $env:APPDATA = $roaming
    $env:WINSHELL_USER_PATH_FILE = Join-Path $root 'user-path.txt'
    $env:WINSHELL_AUTOSTART_FILE = Join-Path $root 'autostart.txt'
    $realLocal = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::Equals([IO.Path]::GetFullPath($local), [IO.Path]::GetFullPath($realLocal), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The test profile resolved to the real LOCALAPPDATA.'
    }
    return [pscustomobject]@{
        Local         = $local
        Roaming       = $roaming
        Root          = Join-Path $local 'Programs\winshell'
        Bin           = Join-Path $local 'Programs\winshell\bin'
        PathFile      = $env:WINSHELL_USER_PATH_FILE
        AutostartFile = $env:WINSHELL_AUTOSTART_FILE
        Shortcut      = Join-Path $roaming 'Microsoft\Windows\Start Menu\Programs\Winshell.lnk'
    }
}

function Read-WinshellTestPath {
    if (-not (Test-Path -LiteralPath $env:WINSHELL_USER_PATH_FILE)) { return '' }
    return [IO.File]::ReadAllText($env:WINSHELL_USER_PATH_FILE, [Text.UTF8Encoding]::new($false))
}

function New-WinshellTestZip {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Marker,
        [string[]] $Names = @('winshell.exe', 'winshellw.exe', 'winshell.dll'),
        [string[]] $ExtraEntries = @()
    )
    Add-WinshellCompressionAssembly
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @($Names + $ExtraEntries)) {
            $entry = $zip.CreateEntry($name)
            $stream = $entry.Open()
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($Marker)
                $stream.Write($bytes, 0, $bytes.Length)
            } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
}

function Get-WinshellScriptCommandName([string] $Path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors -and @($errors).Count -gt 0) { throw "Parse error in ${Path}: $errors" }
    $commands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
    return @($commands | ForEach-Object { $_.GetCommandName() })
}

function ConvertTo-WinshellProcessArgument([string[]] $Argument) {
    return (($Argument | ForEach-Object {
                if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
            }) -join ' ')
}

function Invoke-WinshellScriptFile {
    param(
        [string] $File,
        [string[]] $Argument = @(),
        [hashtable] $Environment = @{},
        [string] $Command
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Process -Id $PID).Path
    if (-not [string]::IsNullOrEmpty($Command)) {
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
        $all = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    } else {
        $all = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $File) + $Argument
    }
    $psi.Arguments = ConvertTo-WinshellProcessArgument $all
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($name in @($Environment.Keys)) {
        $key = [string]$name
        $value = [string]$Environment[$key]
        # StringDictionary has no Contains method. Calling a missing method enumerates it.
        if ([string]::IsNullOrEmpty($value)) {
            [void]$psi.EnvironmentVariables.Remove($key)
        } else {
            $psi.EnvironmentVariables[$key] = $value
        }
    }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit(90000)) {
        try { $proc.Kill() } catch { }
        throw "Timed out running $File"
    }
    [void]$proc.WaitForExit()
    return [pscustomobject]@{
        ExitCode = $proc.ExitCode
        Output   = $outTask.Result
        Error    = $errTask.Result
    }
}

function Start-WinshellFakeRelease([string] $Directory) {
    $ready = [hashtable]::Synchronized(@{ Base = $null; Error = $null; Listener = $null; Log = $null })
    $gate = [System.Threading.ManualResetEventSlim]::new($false)
    $log = Join-Path ([IO.Path]::GetTempPath()) ('winshell-fake-release-' + [guid]::NewGuid().ToString('n') + '.log')
    $rs = [runspacefactory]::CreateRunspace()
    $rs.Open()
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    [void]$ps.AddScript({
            param($Dir, $Ready, $Gate, $Log)
            try {
                $tcp = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback, 0)
                $tcp.Start()
                $port = ([Net.IPEndPoint]$tcp.LocalEndpoint).Port
                $tcp.Stop()
                $listener = New-Object System.Net.HttpListener
                $listener.Prefixes.Add("http://127.0.0.1:${port}/")
                $listener.Start()
                $Ready.Listener = $listener
                $Ready.Base = "http://127.0.0.1:${port}/releases"
                $Ready.Log = $Log
                $Gate.Set()
                while ($listener.IsListening) {
                    try { $ctx = $listener.GetContext() } catch { break }
                    try {
                        $leaf = [IO.Path]::GetFileName($ctx.Request.Url.LocalPath)
                        [IO.File]::AppendAllText($Log, $ctx.Request.RawUrl + "`n")
                        $file = Join-Path $Dir $leaf
                        if ($leaf -and (Test-Path -LiteralPath $file)) {
                            $bytes = [IO.File]::ReadAllBytes($file)
                            $ctx.Response.StatusCode = 200
                            $ctx.Response.ContentType = 'application/octet-stream'
                            $ctx.Response.ContentLength64 = $bytes.LongLength
                            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                        } else {
                            $ctx.Response.StatusCode = 404
                        }
                    } catch {
                        try { $ctx.Response.StatusCode = 500 } catch { }
                    } finally {
                        try { $ctx.Response.OutputStream.Close() } catch { }
                    }
                }
            } catch {
                $Ready.Error = $_.Exception.Message
                try { $Gate.Set() } catch { }
            }
        }).AddArgument($Directory).AddArgument($ready).AddArgument($gate).AddArgument($log)
    $handle = $ps.BeginInvoke()
    if (-not $gate.Wait(10000)) { throw 'The fake release server did not start.' }
    if ($ready.Error) { throw $ready.Error }
    return [pscustomobject]@{
        Base       = [string]$ready.Base
        Log        = $log
        Ready      = $ready
        PowerShell = $ps
        Runspace   = $rs
        Handle     = $handle
        Gate       = $gate
    }
}

function Stop-WinshellFakeRelease($Server) {
    if (-not $Server) { return }
    if ($Server.Ready.Listener) {
        try { $Server.Ready.Listener.Stop() } catch { }
        try { $Server.Ready.Listener.Close() } catch { }
    }
    try { $Server.PowerShell.EndInvoke($Server.Handle) | Out-Null } catch { }
    try { $Server.PowerShell.Dispose() } catch { }
    try { $Server.Runspace.Close() } catch { }
    try { $Server.Runspace.Dispose() } catch { }
    try { $Server.Gate.Dispose() } catch { }
    if ($Server.Log -and (Test-Path -LiteralPath $Server.Log)) {
        Remove-Item -LiteralPath $Server.Log -Force -ErrorAction SilentlyContinue
    }
}
