BeforeAll {
    # package.ps1 builds a release when it runs, so take New-Zip out of its syntax tree and define only that.
    $script = Join-Path $PSScriptRoot '../../scripts/package.ps1'
    $tree = [Management.Automation.Language.Parser]::ParseFile($script, [ref]$null, [ref]$null)
    $function = $tree.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-Zip' }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
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
