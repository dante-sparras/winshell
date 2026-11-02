# Prepares this repo. Git does not run scripts on clone.
# After cloning, run: pwsh scripts/dev.ps1
#
#   pwsh scripts/dev.ps1    checks that the .NET SDK is on PATH
$ErrorActionPreference = 'Stop'

dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'dotnet is not on PATH' }
