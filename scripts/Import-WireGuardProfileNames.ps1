# Imports WireGuard tunnel names only into DeskPilot's unprivileged catalog.
# Run only in a PowerShell session authorized to enumerate the directory.
# Never opens, decrypts, copies, or modifies .conf.dpapi contents.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$source = Join-Path $env:ProgramFiles 'WireGuard\Data\Configurations'
$destination = Join-Path $env:LOCALAPPDATA 'DeskPilot\WireGuardPreview\profiles.txt'
$names = @(Get-ChildItem -LiteralPath $source -Filter '*.conf.dpapi' -File -ErrorAction Stop |
  ForEach-Object { $_.Name.Substring(0, $_.Name.Length - '.conf.dpapi'.Length) } |
  Sort-Object -Unique)
if ($names.Count -eq 0) { throw 'No WireGuard tunnel names found. No catalog written.' }
$directory = Split-Path -Parent $destination
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$names | Set-Content -LiteralPath $destination -Encoding UTF8
Write-Host ("Imported {0} WireGuard tunnel names into DeskPilot's local catalog." -f $names.Count)
Write-Host "No WireGuard configuration contents were accessed or changed."
