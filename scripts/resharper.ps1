#requires -Version 7.0
# Cleanup edits source; Check formats an isolated copy; Inspect reports suggestions and above.
[CmdletBinding(DefaultParameterSetName = 'All')]
param(
    [ValidateSet('Cleanup', 'Check', 'Inspect')]
    [string] $Mode = 'Check',
    [Parameter(Mandatory, ParameterSetName = 'Include')]
    [string[]] $Include,
    [Parameter(Mandatory, ParameterSetName = 'Staged')]
    [switch] $Staged,
    [string] $Output = 'artifacts/resharper'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$session = [IO.Path]::GetFullPath((Join-Path $temporary ('winshell-resharper-' + [guid]::NewGuid().ToString('N'))))
$status = 0

function Invoke-Dotnet {
    param([string[]] $Arguments)
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Get-GitPaths {
    param([string[]] $Arguments)
    $text = [string]::Join("`n", @(git @Arguments))
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate source files with Git.' }
    @($text -split "`0" | Where-Object Length -GT 0 | Sort-Object -Unique)
}

function Restore-TemplateReportPaths {
    param($Node)
    if ($Node -is [pscustomobject]) {
        foreach ($property in $Node.PSObject.Properties) {
            if ($property.Name -eq 'uri' -and $property.Value -is [string] -and
                $property.Value.StartsWith('template/', [StringComparison]::Ordinal)) {
                $property.Value = 'templates/winshell/' + $property.Value.Substring('template/'.Length)
            }
            else { Restore-TemplateReportPaths -Node $property.Value }
        }
    }
    elseif ($Node -is [Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) { Restore-TemplateReportPaths -Node $item }
    }
}

function Invoke-Processing {
    param([string] $Solution, [string[]] $Paths, [string] $ReportName)
    Invoke-Dotnet -Arguments @('build', $Solution, '-warnaserror', '--nologo')
    $arguments = @(
        $(if ($Mode -eq 'Inspect') { 'inspectcode' } else { 'cleanupcode' }),
        $Solution,
        "--settings=$settings",
        "--dotnetcoresdk=$sdk",
        '--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal',
        '--exclude=**/bin/**;**/obj/**;**/*.g.cs;**/*.g.i.cs;**/*.generated.cs',
        "--include=$($Paths -join ';')",
        "--caches-home=$(Join-Path $session 'caches')",
        '--no-build',
        '--no-updates'
    )
    if ($Mode -eq 'Inspect') {
        $report = Join-Path $reportDirectory "$ReportName.sarif"
        $arguments += @("--output=$report", '--format=Sarif', '--severity=SUGGESTION')
    }
    else {
        $arguments += '--profile=Winshell'
    }
    if ($Mode -eq 'Cleanup') {
        # Body-style changes can expose another syntax-style change on the following pass.
        # Stop only at a fixed point, and report a cycling/broken profile instead of looping forever.
        $folder = Split-Path -Parent $Solution
        $stable = $false
        for ($pass = 0; $pass -lt 5; $pass++) {
            $hashes = @{}
            foreach ($path in $Paths) { $hashes[$path] = (Get-FileHash -LiteralPath (Join-Path $folder $path)).Hash }
            Invoke-Dotnet -Arguments (@('tool', 'run', 'jb', '--') + $arguments)
            $changed = @($Paths | Where-Object { (Get-FileHash -LiteralPath (Join-Path $folder $_)).Hash -ne $hashes[$_] })
            if ($changed.Count -eq 0) { $stable = $true; break }
        }
        if (-not $stable) { throw "ReSharper cleanup did not stabilize after five passes: $Solution" }
    }
    else {
        Invoke-Dotnet -Arguments (@('tool', 'run', 'jb', '--') + $arguments)
    }
    if ($Mode -eq 'Inspect') {
        $sarif = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($ReportName -eq 'template') {
            foreach ($run in $sarif.runs) {
                if ($null -ne $run.PSObject.Properties['originalUriBaseIds'] -and
                    $null -ne $run.originalUriBaseIds.PSObject.Properties['solutionDir']) {
                    $run.originalUriBaseIds.solutionDir.uri = ([uri]::new($root + [IO.Path]::DirectorySeparatorChar)).AbsoluteUri
                }
                Restore-TemplateReportPaths -Node $run
            }
            $sarif | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
        }
        $results = @($sarif.runs | ForEach-Object results)
        Write-Host "$($results.Count) findings: $report"
        if ($results.Count -gt 0) { $script:status = 1 }
    }
}

Push-Location $root
try {
    $paths = @(Get-GitPaths -Arguments @('ls-files', '--cached', '--others', '--exclude-standard', '-z'))
    $sources = @($paths | Where-Object {
        ($_ -match '^(src/|tests/|templates/winshell/)' -or $_ -in @('templates/TemplatePackage.cs', 'scripts/generate-reference.cs')) -and
        $_ -match '\.(cs|xaml)$' -and $_ -notmatch '(^|/)(bin|obj)/|\.(g|g\.i|generated)\.cs$' -and
        (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)
    })
    $selected = $sources
    if ($Staged) {
        $stagedPaths = @(Get-GitPaths -Arguments @('diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z'))
        $selected = @($sources | Where-Object { $_ -in $stagedPaths })
    }
    elseif ($PSCmdlet.ParameterSetName -eq 'Include') {
        $selected = @($Include | ForEach-Object {
            $full = [IO.Path]::GetFullPath($_, $root)
            $relative = [IO.Path]::GetRelativePath($root, $full).Replace('\', '/')
            if ($relative -notin $sources) { throw "Not a handwritten solution or template source: $_" }
            $relative
        } | Sort-Object -Unique)
    }
    if ($selected.Count -gt 0) {
        New-Item -ItemType Directory -Path $session | Out-Null
        Invoke-Dotnet -Arguments @('tool', 'restore', '--tool-manifest', (Join-Path $root '.config/dotnet-tools.json'))
        $workRoot = $root
        if ($Mode -eq 'Check') {
            $workRoot = Join-Path $session 'source'
            foreach ($path in $paths) {
                $source = Join-Path $root $path
                if (-not [IO.File]::Exists($source)) { continue }
                $destination = Join-Path $workRoot $path
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                [IO.File]::Copy($source, $destination)
            }
        }
        $settings = Join-Path $workRoot 'winshell.slnx.DotSettings'
        $sdk = (Get-Content -LiteralPath (Join-Path $workRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
        $reportDirectory = [IO.Path]::GetFullPath($Output, $root)
        if ($Mode -eq 'Inspect') { [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null }
        $before = @{}
        if ($Mode -eq 'Check') {
            foreach ($path in $selected) { $before[$path] = (Get-FileHash -LiteralPath (Join-Path $workRoot $path)).Hash }
        }
        $mainPaths = @($selected | Where-Object { -not $_.StartsWith('templates/winshell/', [StringComparison]::Ordinal) })
        if ($mainPaths.Count -gt 0) {
            Invoke-Processing -Solution (Join-Path $workRoot 'winshell.slnx') -Paths $mainPaths -ReportName solution
        }
        $templatePaths = @($selected | Where-Object { $_.StartsWith('templates/winshell/', [StringComparison]::Ordinal) })
        if ($templatePaths.Count -gt 0) {
            # Load real template code with the current library API, without changing its version placeholder
            # or installing a template into the contributor's personal dotnet-new configuration.
            $templateRoot = Join-Path $session 'template'
            [IO.Directory]::CreateDirectory($templateRoot) | Out-Null
            foreach ($path in $paths | Where-Object { $_.StartsWith('templates/winshell/', [StringComparison]::Ordinal) }) {
                $source = Join-Path $workRoot $path
                if (-not [IO.File]::Exists($source)) { continue }
                $relative = $path.Substring('templates/winshell/'.Length)
                $destination = Join-Path $templateRoot $relative
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                [IO.File]::Copy($source, $destination)
            }
            Copy-Item -LiteralPath (Join-Path $workRoot '.editorconfig') -Destination (Join-Path $session '.editorconfig')
            Copy-Item -LiteralPath (Join-Path $workRoot 'global.json') -Destination (Join-Path $session 'global.json')
            $projectPath = Join-Path $templateRoot 'MyShell.csproj'
            $project = [xml](Get-Content -LiteralPath $projectPath -Raw)
            $package = $project.SelectSingleNode('/Project/ItemGroup/PackageReference[@Include="Winshell"]')
            if ($null -eq $package) { throw 'The template no longer references the Winshell package.' }
            $reference = $project.CreateElement('ProjectReference')
            $reference.SetAttribute('Include', (Join-Path $workRoot 'src/Winshell.Shell/Winshell.Shell.csproj'))
            $package.ParentNode.ReplaceChild($reference, $package) | Out-Null
            $project.Save($projectPath)
            $solution = Join-Path $session 'template.slnx'
            [IO.File]::WriteAllText($solution, '<Solution><Project Path="template/MyShell.csproj" /></Solution>')
            $relativePaths = @($templatePaths | ForEach-Object { 'template/' + $_.Substring('templates/winshell/'.Length) })
            Invoke-Processing -Solution $solution -Paths $relativePaths -ReportName template
            if ($Mode -ne 'Inspect') {
                foreach ($path in $templatePaths) {
                    [IO.File]::Copy((Join-Path $templateRoot $path.Substring('templates/winshell/'.Length)), (Join-Path $workRoot $path), $true)
                }
            }
        }
        if ($Mode -eq 'Check') {
            $changed = @($selected | Where-Object { (Get-FileHash -LiteralPath (Join-Path $workRoot $_)).Hash -ne $before[$_] })
            if ($changed.Count -gt 0) {
                Write-Host 'ReSharper cleanup would change:'
                $changed | ForEach-Object { Write-Host "  $_" }
                Write-Host 'Run: pwsh -NoProfile scripts/resharper.ps1 -Mode Cleanup'
                $status = 1
            }
        }
    }
}
catch {
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    $status = 1
}
finally {
    if (Test-Path -LiteralPath $session) {
        $resolved = (Resolve-Path -LiteralPath $session).ProviderPath
        if (-not $resolved.Equals($session, [StringComparison]::OrdinalIgnoreCase) -or
            -not $resolved.StartsWith($temporary.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an unexpected temporary directory: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Pop-Location
}
exit $status
