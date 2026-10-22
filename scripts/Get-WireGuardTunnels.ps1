# DeskPilot WireGuard discovery probe (read-only).
# Lists only Windows service names and statuses. Never reads .conf, private keys,
# tunnel IP addresses, or WireGuard registry configuration.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$services = @(Get-CimInstance Win32_Service -Filter "Name LIKE 'WireGuardTunnel$%'" -ErrorAction Stop)
$rows = foreach ($service in $services) {
    if (-not $service.Name.StartsWith('WireGuardTunnel$', [StringComparison]::OrdinalIgnoreCase)) { continue }
    [pscustomobject]@{
        Tunnel = $service.Name.Substring('WireGuardTunnel$'.Length)
        Status = $service.State
        StartMode = $service.StartMode
    }
}
if ($rows.Count -eq 0) {
    Write-Host 'No installed WireGuard tunnel services found.'
    Write-Host 'Tunnels visible only in the WireGuard UI may not be installed as Windows services.'
} else {
    $rows | Sort-Object Tunnel | Format-Table -AutoSize
}
Write-Host 'Read-only scan completed; no tunnels were started, stopped, or modified.'
