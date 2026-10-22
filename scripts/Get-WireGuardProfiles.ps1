# Read-only discovery of official WireGuard Windows profiles.
# Does not open, decrypt, copy, or change protected .conf.dpapi files.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$folder = Join-Path $env:ProgramFiles 'WireGuard\Data\Configurations'
if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
    Write-Error "WireGuard configuration directory not accessible: $folder"
    exit 1
}
$files = @(Get-ChildItem -LiteralPath $folder -File -Filter '*.conf.dpapi' -ErrorAction Stop)
$names = @($files | ForEach-Object { $_.Name.Substring(0, $_.Name.Length - '.conf.dpapi'.Length) } | Sort-Object -Unique)
Write-Host ("Found {0} protected WireGuard profiles:" -f $names.Count)
$names | ForEach-Object { Write-Host ("  " + $_) }
Write-Host 'Read-only discovery complete. No configuration contents were accessed.'
