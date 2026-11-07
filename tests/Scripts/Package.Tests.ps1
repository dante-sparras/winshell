BeforeAll {
    # package.ps1 builds a release when it runs, so take the functions under test out of its syntax tree and define only those.
    $script = Join-Path $PSScriptRoot '../../scripts/package.ps1'
    $tree = [Management.Automation.Language.Parser]::ParseFile($script, [ref]$null, [ref]$null)
    foreach ($name in 'New-Zip', 'Get-PeLayout') {
        $function = $tree.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
        . ([scriptblock]::Create($function.Extent.Text))
    }
    $releaseDay = [datetime]::new(2026, 11, 2)
}

Describe 'Packing a zip' {
    It 'orders entries by ordinal path under a culture that sorts differently, and dates them the release day' {
        $dir = Join-Path $TestDrive 'files'
        New-Item -ItemType Directory -Path (Join-Path $dir 'sub') | Out-Null
        foreach ($name in 'y', 'i', [string][char]0x012F, 'sub/a', 'Z') {
            Set-Content -LiteralPath (Join-Path $dir $name) -Value $name
        }
        $zip = Join-Path $TestDrive 'out.zip'
        $culture = [Threading.Thread]::CurrentThread.CurrentCulture
        [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('lt-LT')
        try { New-Zip $dir $zip } finally { [Threading.Thread]::CurrentThread.CurrentCulture = $culture }

        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            $archive.Entries.FullName -join ',' | Should -BeExactly "Z,i,sub/a,y,$([char]0x012F)"
            $archive.Entries.LastWriteTime.Date | Select-Object -Unique | Should -Be ([DateTimeOffset]::new($releaseDay, [TimeSpan]::Zero).Date)
        } finally { $archive.Dispose() }
    }
}

Describe 'Reading a PE header' {
    It 'finds the machine and subsystem .NET reads from this PowerShell' {
        $path = (Get-Process -Id $PID).Path
        $bytes = [IO.File]::ReadAllBytes($path)
        $stream = [IO.File]::OpenRead($path)
        try { $headers = [Reflection.PortableExecutable.PEHeaders]::new($stream) } finally { $stream.Dispose() }
        $layout = Get-PeLayout $bytes

        [BitConverter]::ToUInt16($bytes, $layout.Machine) | Should -Be ([int] $headers.CoffHeader.Machine)
        [BitConverter]::ToUInt16($bytes, $layout.Subsystem) | Should -Be ([int] $headers.PEHeader.Subsystem)
    }
}

Describe 'The binaries Winshell signs' {
    It 'are the same for each runtime in SignPath''s artifact configuration, and include every assembly built from src' {
        [xml] $config = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../../packaging/signpath/artifact-configuration.xml')
        $runtimes = $config.'artifact-configuration'.'zip-file'.'pe-file-set'.include.path | Group-Object { ($_ -split '/')[0] }
        # The package project only packs the others; it builds nothing that ships.
        $assemblies = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../../src') -Recurse -Filter '*.csproj' | ForEach-Object {
            $text = Get-Content -Raw -LiteralPath $_.FullName
            if ($text -notmatch '<IsPackable>true</IsPackable>') {
                ($text -match '<AssemblyName>([^<]+)</AssemblyName>' ? $Matches[1] : $_.BaseName) + '.dll'
            }
        }

        $runtimes.Name | Sort-Object | Should -Be 'win-arm64', 'win-x64'
        $binaries = $runtimes[0].Group | ForEach-Object { ($_ -split '/')[1] } | Sort-Object
        $runtimes[1].Group | ForEach-Object { ($_ -split '/')[1] } | Sort-Object | Should -Be $binaries
        $assemblies | Should -Not -BeNullOrEmpty
        foreach ($assembly in $assemblies) { $binaries | Should -Contain $assembly }
    }
}

Describe 'The oldest Windows a release runs on' {
    It 'is Windows 11 22H2 wherever the build, a project, a manifest or doctor states it' {
        $root = Join-Path $PSScriptRoot '../..'
        $windows = '<(?:SupportedOSPlatformVersion>|TargetFramework>net[\d.]+-windows)(10\.0\.\d+\.\d+)<'
        $projects = Get-ChildItem -LiteralPath (Join-Path $root 'src'), (Join-Path $root 'tests'), (Join-Path $root 'templates') -Recurse -File -Include '*.csproj', '*.props' |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        $doctor = Select-String -LiteralPath (Join-Path $root 'src/Winshell.Services/Diagnostics/Doctor.cs') -Pattern 'build < (\d+)\)' |
            ForEach-Object { [int] $_.Matches[0].Groups[1].Value } | Measure-Object -Maximum
        $declared = @(
            (Select-String -LiteralPath (Join-Path $root 'Directory.Build.props') -Pattern $windows).Matches[0].Groups[1].Value
            $projects | Select-String -Pattern $windows | ForEach-Object { $_.Matches[0].Groups[1].Value }
            (Select-String -LiteralPath (Join-Path $root 'scripts/package.ps1') -Pattern '^MinimumOSVersion: (\S+)').Matches[0].Groups[1].Value
            ([xml] (Get-Content -Raw -LiteralPath (Join-Path $root 'packaging/msix/AppxManifest.xml'))).Package.Dependencies.TargetDeviceFamily.MinVersion
            "10.0.$($doctor.Maximum).0"
        )

        $declared | Select-Object -Unique | Should -Be '10.0.22621.0'
    }
}
