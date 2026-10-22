# Read-only status for official WireGuard Windows tunnel services.
# Does not access tunnel configurations, keys, or modify services.
[CmdletBinding()]
param(
    [string]$Tunnel
)
$ErrorActionPreference = 'Stop'
$services = @(Get-CimInstance Win32_Service -Filter "Name LIKE 'WireGuardTunnel$%'" -ErrorAction Stop)
$rows = @($services | Where-Object {
    $_.Name.StartsWith('WireGuardTunnel$', [StringComparison]::OrdinalIgnoreCase)
} | ForEach-Object {
    [pscustomobject]@{
        Tunnel = $_.Name.Substring('WireGuardTunnel$'.Length)
        Status = $_.State
        ServiceName = $_.Name
    }
})
if ($Tunnel) {
    $rows = @($rows | Where-Object { $_.Tunnel -eq $Tunnel })
    if ($rows.Count -eq 0) {
        Write-Host "No active/installed tunnel service found for '$Tunnel'. This does not prove the profile is absent."
    }
} elseif ($rows.Count -eq 0) {
    Write-Host 'No WireGuard tunnel services found.'
}
$rows | Sort-Object Tunnel | Format-Table -AutoSize
Write-Host 'Read-only status check complete.'
