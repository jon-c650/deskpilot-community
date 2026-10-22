# Proof of concept only. Not part of the DeskPilot application yet.
# Tests whether Microsoft RDP drive redirection can see a SUBST mapping.
# Run in a normal (non-elevated) PowerShell session, under the same Windows user as DeskPilot.
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Mount','Unmount','Status')]
    [string]$Action,
    [Parameter(Mandatory=$true)]
    [string]$Folder,
    [ValidatePattern('^[A-Za-z]$')]
    [string]$Letter = 'R'
)
$ErrorActionPreference = 'Stop'
$letter = $Letter.ToUpperInvariant()
$drive = "$($letter):"
$full = [System.IO.Path]::GetFullPath($Folder).TrimEnd('\')
$root = [System.IO.Path]::GetPathRoot($full).TrimEnd('\')
if ($full -eq $root) { throw 'Refusing to expose a drive root. Choose a dedicated test folder.' }
if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Folder does not exist: $full" }
$dir = Get-Item -LiteralPath $full -Force
if ($dir.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Do not use a symlink, junction, or reparse-point folder for this test.' }
function Get-Mapping {
    $lines = & subst.exe
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate SUBST mappings.' }
    foreach ($line in $lines) {
        if ($line.StartsWith($drive + '\: => ', [StringComparison]::OrdinalIgnoreCase)) {
            return $line.Substring(($drive + '\: => ').Length).TrimEnd('\')
        }
    }
    return $null
}
$current = Get-Mapping
switch ($Action) {
    'Status' {
        if ($null -eq $current) { Write-Host "No SUBST mapping on $drive" }
        else { Write-Host "$drive maps to $current" }
    }
    'Mount' {
        if ($null -ne $current) { throw "$drive already has a SUBST mapping: $current" }
        if (Test-Path "$drive\") { throw "$drive is already in use. Choose another letter." }
        & subst.exe $drive $full
        if ($LASTEXITCODE -ne 0) { throw 'SUBST failed.' }
        Write-Host "Created temporary mapping $drive -> $full"
        Write-Host "In DeskPilot, redirect ONLY drive $drive (not All Drives)."
        Write-Host "Reconnect RDP and check \\tsclient\$letter in the remote session."
        Write-Host "After disconnecting, run this script with -Action Unmount and the same -Folder and -Letter."
    }
    'Unmount' {
        if ($null -eq $current) { Write-Host "No SUBST mapping exists for $drive"; break }
        if (-not [string]::Equals($current,$full,[StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove $drive because it points to '$current', not '$full'."
        }
        & subst.exe $drive /D
        if ($LASTEXITCODE -ne 0) { throw "Could not remove mapping $drive" }
        Write-Host "Removed mapping $drive"
    }
}
