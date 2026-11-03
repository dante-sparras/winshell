# Publishes a winshell inspect screenshot for a pull request, and prints the Markdown to paste into it. Pictures go on the
# pr-screenshots branch, which never merges, so they stay out of main's history.
#
#   pwsh scripts/pr-screenshot.ps1 out.png
param([Parameter(Mandatory)][string] $File)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = gh repo view --json nameWithOwner --jq .nameWithOwner
$branch = 'pr-screenshots'
gh api "repos/$repository/git/ref/heads/$branch" --silent 2>$null
if ($LASTEXITCODE -ne 0) {
    $main = gh api "repos/$repository/git/ref/heads/main" --jq .object.sha
    gh api "repos/$repository/git/refs" -f "ref=refs/heads/$branch" -f "sha=$main" --silent
    if ($LASTEXITCODE -ne 0) { throw "Could not make the $branch branch." }
}
# Each upload has its own name, so a retake doesn't change the picture in a pull request that shows the first one.
$name = "$(git rev-parse --short HEAD)-$([guid]::NewGuid().ToString('N').Substring(0, 8))-$([IO.Path]::GetFileName($File))"
$content = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $File)))
$body = @{ message = "Screenshot $name"; branch = $branch; content = $content } | ConvertTo-Json -Compress
$url = $body | gh api -X PUT "repos/$repository/contents/$name" --input - --jq .content.download_url
if ($LASTEXITCODE -ne 0) { throw "Could not upload $name." }
"![$([IO.Path]::GetFileNameWithoutExtension($File))]($url)"
