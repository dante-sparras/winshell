# Run the test projects under tests/. Passes when none exist yet.
#   pwsh -NoProfile -File scripts/test.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tests = Join-Path $root 'tests'
if (-not (Test-Path $tests)) {
    Write-Output 'No tests directory. The test lane passes until a project exists under tests/.'
    exit 0
}
$projects = @(Get-ChildItem -Path $tests -Filter *.csproj -Recurse -File)
if ($projects.Count -eq 0) {
    Write-Output 'No test project under tests/. The test lane passes until one is added.'
    exit 0
}
foreach ($project in $projects) {
    Write-Output "dotnet test $($project.FullName)"
    dotnet test $project.FullName --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
