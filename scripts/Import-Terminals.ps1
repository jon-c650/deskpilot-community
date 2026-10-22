# DeskPilot Terminals source import — run from a Windows PowerShell terminal with Git installed.
# Imports upstream into a subdirectory while preserving original history through git subtree.
# Does NOT touch the installed Terminals program or its configuration store.
$ErrorActionPreference = 'Stop'
$repo = 'https://github.com/jon-c650/deskpilot.git'
$upstream = 'https://github.com/Terminals-Origin/Terminals.git'
$root = Join-Path $HOME 'source'
$destination = Join-Path $root 'deskpilot'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows is required. Install Git and rerun this script.'
}
New-Item -ItemType Directory -Force -Path $root | Out-Null
if (Test-Path $destination) {
    throw "Destination already exists: $destination. Use a fresh folder or inspect the existing checkout."
}
git clone $repo $destination
if ($LASTEXITCODE -ne 0) { throw 'DeskPilot clone failed.' }
Push-Location $destination
try {
    git remote add terminals-upstream $upstream
    if ($LASTEXITCODE -ne 0) { throw 'Could not add upstream remote.' }
    git fetch terminals-upstream master
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch Terminals master.' }
    $sha = (git rev-parse terminals-upstream/master).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve upstream SHA.' }
    git subtree add --prefix=upstream/Terminals terminals-upstream master --squash -m "vendor: import Terminals upstream $sha"
    if ($LASTEXITCODE -ne 0) { throw 'Git subtree import failed. No push performed.' }
    Set-Content -Path UPSTREAM_REVISION.txt -Value @("Source: $upstream", "Ref: master", "Commit: $sha")
    git add UPSTREAM_REVISION.txt
    git commit -m 'docs: record pinned Terminals upstream revision'
    if ($LASTEXITCODE -ne 0) { throw 'Could not record upstream revision.' }
    Write-Host "Upstream source imported at $sha"
    Write-Host 'Review the staged source and license before pushing.'
    git status --short
    $answer = Read-Host 'Push imported source to your private DeskPilot repository? (type YES)'
    if ($answer -ceq 'YES') {
        git push origin main
        if ($LASTEXITCODE -ne 0) { throw 'Push failed.' }
        Write-Host 'Source imported and pushed.'
    } else {
        Write-Host "No push performed. Local checkout: $destination"
    }
} finally { Pop-Location }
