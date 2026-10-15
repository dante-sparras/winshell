# Lint the tip the pull request will lint. One run, not one run per commit.
#   pwsh -NoProfile -File scripts/check.ps1
#   pwsh -NoProfile -File scripts/check.ps1 -InstallHook
#   pwsh -NoProfile -File scripts/check.ps1 -Ref <sha>
param(
    [string]$Ref,
    [switch]$InstallHook
)

$ErrorActionPreference = 'Stop'

function Install-PrePushHook {
    $hooks = git rev-parse --git-path hooks
    if ($LASTEXITCODE -ne 0) { throw 'This directory is not a git checkout.' }
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $path = Join-Path $hooks 'pre-push'
    $body = @"
#!/bin/sh
# Lint each pushed branch tip once. A tag push is not linted.
root=`$(git rev-parse --show-toplevel) || exit 1
status=0
while read -r local_ref local_sha remote_ref remote_sha; do
    if [ "`$local_sha" = "0000000000000000000000000000000000000000" ]; then
        continue
    fi
    case "`$remote_ref" in
        refs/tags/*) continue ;;
    esac
    '$pwsh' -NoProfile -File "`$root/scripts/check.ps1" -Ref "`$local_sha" || status=1
done
exit "`$status"
"@
    $body = $body.Replace("`r`n", "`n")
    [System.IO.File]::WriteAllText($path, $body)
    Write-Output "Installed $path"
}

if ($InstallHook) {
    Install-PrePushHook
    if (-not $Ref) { exit 0 }
}

$scratch = $null
$root = Split-Path $PSScriptRoot -Parent
if ($Ref) {
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("winshell-check-" + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $root = Join-Path $scratch 'tree'
    New-Item -ItemType Directory -Path $root | Out-Null
}

function Get-LintFiles([string]$start, [string]$filter) {
    $found = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($start)
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        $name = Split-Path $dir -Leaf
        if ($name -in @('.git', 'bin', 'obj', 'quickshell', 'StrykerOutput', 'node_modules', '__pycache__')) {
            continue
        }
        foreach ($file in [System.IO.Directory]::EnumerateFiles($dir, $filter)) {
            $found.Add($file)
        }
        foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) {
            $stack.Push($sub)
        }
    }
    return $found
}

$failed = 0
try {
    if ($Ref) {
        $listed = @(git ls-tree -r --name-only $Ref)
        if ($LASTEXITCODE -ne 0) { throw "git ls-tree $Ref failed" }
        foreach ($rel in $listed) {
            if ($rel -notmatch '\.(py|ps1)$') { continue }
            $dest = Join-Path $root ($rel.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
            $parent = Split-Path $dest -Parent
            if (-not (Test-Path $parent)) {
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
            }
            $proc = Start-Process -FilePath git -ArgumentList @('-c', 'core.pager=', 'cat-file', '-p', "${Ref}:${rel}") -RedirectStandardOutput $dest -NoNewWindow -Wait -PassThru
            if ($proc.ExitCode -ne 0) { throw "git cat-file ${Ref}:${rel} failed" }
        }
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) { throw 'python is not on PATH. The lint compiles docs/fetch_reference.py.' }
    $env:PYTHONPYCACHEPREFIX = Join-Path ([System.IO.Path]::GetTempPath()) 'winshell-pyc'

    $pyFiles = @(Get-LintFiles $root '*.py')
    if ($pyFiles.Count -eq 0) {
        Write-Output 'No Python files to compile.'
    }
    foreach ($file in $pyFiles) {
        Write-Output "python -m py_compile $file"
        python -m py_compile $file
        if ($LASTEXITCODE -ne 0) { $failed = 1 }
    }

    $psFiles = @(Get-LintFiles $root '*.ps1')
    foreach ($file in $psFiles) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
        if ($errors -and $errors.Count -gt 0) {
            Write-Output "PowerShell parse failed: $file"
            $errors | ForEach-Object { Write-Output $_.ToString() }
            $failed = 1
        } else {
            Write-Output "powershell parse $file"
        }
    }
} finally {
    if ($scratch) { Remove-Item -Recurse -Force $scratch }
}

exit $failed
