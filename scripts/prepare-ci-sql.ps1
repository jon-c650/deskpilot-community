$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable SQL fixture is CI-only' }
$localdb = Get-ChildItem 'C:/Program Files/Microsoft SQL Server' -Filter SqlLocalDB.exe -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $localdb) { throw 'SQL LocalDB missing on Windows runner' }
& $localdb.FullName create DeskPilotBetaCi
if ($LASTEXITCODE -ne 0) { throw 'Cannot create disposable LocalDB instance' }
& $localdb.FullName start DeskPilotBetaCi
if ($LASTEXITCODE -ne 0) { throw 'Cannot start disposable LocalDB instance' }
$master = New-Object System.Data.SqlClient.SqlConnection 'Data Source=(localdb)\DeskPilotBetaCi;Initial Catalog=master;Integrated Security=True;Connect Timeout=30'
try {
    $master.Open()
    $command = $master.CreateCommand()
    $command.CommandText = 'CREATE DATABASE [DeskPilotCiFixture]'
    $null = $command.ExecuteNonQuery()
} finally {$master.Dispose()}
'DESKPILOT_CI_SQL=Data Source=(localdb)\DeskPilotBetaCi;Initial Catalog=DeskPilotCiFixture;Integrated Security=True;Connect Timeout=30' | Add-Content $env:GITHUB_ENV

('DESKPILOT_CI_SOURCE_ROOT=' + (Split-Path $PSScriptRoot)) | Add-Content $env:GITHUB_ENV
