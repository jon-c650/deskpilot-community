$ErrorActionPreference = 'Stop'
$policy = Get-Content (Join-Path $PSScriptRoot '../packaging/public-release.json') -Raw | ConvertFrom-Json
if ($policy.publicDistributionApproved -ne $true -or @($policy.blockers).Count -ne 0) {
    $details = @($policy.blockers | ForEach-Object { "$($_.id): $($_.reason)" }) -join "`n"
    throw "Public distribution is not cleared. Do not publish the private-test installer.`n$details"
}
