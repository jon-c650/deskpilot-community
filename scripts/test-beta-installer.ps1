$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer regression tests run only in disposable GitHub runners.' }
$repo = Split-Path $PSScriptRoot
$setups = @(Get-ChildItem (Join-Path $repo 'installer-output/*-Setup.exe'))
if ($setups.Count -ne 1) { throw 'Expected one installer' }
$setup = $setups[0]
$target = Join-Path $env:LOCALAPPDATA 'Programs/DeskPilot Beta'
$report = [Collections.Generic.List[string]]::new()
function Run-Setup([string]$label) {
    $p = Start-Process $setup.FullName -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/LOG=`"$repo/ci-logs/installer-$label.log`"") -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup $label failed: $($p.ExitCode)" }
}
# Sentinels are deliberately outside the installation directory, in disposable CI only.
$sentinels = @(
    (Join-Path $env:LOCALAPPDATA 'DeskPilot/IsolatedPreview/installer-data-sentinel.bin'),
    (Join-Path $env:LOCALAPPDATA 'DeskPilot/WireGuardPreview/installer-vpn-sentinel.bin'),
    (Join-Path $env:LOCALAPPDATA 'Terminals/installer-original-data-sentinel.bin')
)
$hashes = @{}
foreach ($file in $sentinels) {
    if (Test-Path $file) { throw 'Unexpected existing CI sentinel' }
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    [IO.File]::WriteAllBytes($file, [Text.Encoding]::UTF8.GetBytes([Guid]::NewGuid().ToString()))
    $hashes[$file] = (Get-FileHash $file).Hash
}
$dataRoots = @(
    (Join-Path $env:LOCALAPPDATA 'DeskPilot/IsolatedPreview'),
    (Join-Path $env:LOCALAPPDATA 'DeskPilot/WireGuardPreview'),
    (Join-Path $env:LOCALAPPDATA 'Terminals')
)
$dataHashes = @{}
foreach ($root in $dataRoots) {
    Get-ChildItem $root -Recurse -File | ForEach-Object { $dataHashes[$_.FullName] = (Get-FileHash $_.FullName).Hash }
}
function Assert-Data {
    $current = @{}
    foreach ($root in $dataRoots) {
        Get-ChildItem $root -Recurse -File | ForEach-Object { $current[$_.FullName] = (Get-FileHash $_.FullName).Hash }
    }
    if ($current.Count -ne $dataHashes.Count) { throw 'Installer added or removed saved data files' }
    foreach ($file in $dataHashes.Keys) {
        if ($current[$file] -ne $dataHashes[$file]) { throw 'Installer modified saved customer, credential or VPN data' }
    }
}
Run-Setup 'first'
Assert-Data
$exe = Join-Path $target 'DeskPilot.Preview.exe'
if (-not (Test-Path $exe)) { throw 'Installed application missing' }
$sources = @(Get-ChildItem (Join-Path $target 'Source/*-Source.zip'))
if ($sources.Count -ne 1) {throw 'Installed corresponding source archive missing'}
$originalSource = Get-ChildItem (Join-Path $repo 'installer-output/*-Source.zip')
if ((Get-FileHash $sources[0].FullName).Hash -ne (Get-FileHash $originalSource.FullName).Hash) {throw 'Installed source differs from downloadable source'}
$report.Add('PASS installed exact matching corresponding source archive')
# A retired application DLL must be removed on upgrade without touching profiles.
[IO.File]::WriteAllText((Join-Path $target 'TabControl.dll'),'retired CI fixture')
$before = (Get-FileHash $exe).Hash
Run-Setup 'reinstall'
if (Test-Path (Join-Path $target 'TabControl.dll')) {throw 'Retired tab dependency retained'}
Assert-Data
if ((Get-FileHash $exe).Hash -ne $before) { throw 'Reinstall changed expected executable' }
$lock = [IO.File]::Open($exe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $p = Start-Process $setup.FullName -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-') -Wait -PassThru
    if ($p.ExitCode -eq 0) { throw 'Installer failed to refuse an in-use application' }
} finally { $lock.Dispose() }
Assert-Data
$report.Add('PASS per-user install, reinstall, in-use refusal and all saved-data file hashes')
$shell = New-Object -ComObject WScript.Shell
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'DeskPilot Beta/DeskPilot Beta.lnk'
if (-not (Test-Path $link) -or $shell.CreateShortcut($link).TargetPath -ne $exe) { throw 'Start Menu shortcut missing or wrong target' }
$report.Add('PASS DeskPilot Start Menu shortcut')
# Test the real installed payload with the existing integrated suite. Isolate the
# earlier synthetic smoke profile so the suite's fresh-profile safeguard remains intact.
$profile = Join-Path $env:LOCALAPPDATA 'DeskPilot/IsolatedPreview'
$backup = Join-Path $env:RUNNER_TEMP ('deskpilot-ci-profile-' + [Guid]::NewGuid())
Move-Item $profile $backup
try {
    $env:DESKPILOT_UI_SMOKE = '1'
    $p = Start-Process $exe -PassThru
    if (-not $p.WaitForExit(180000)) { $p.Kill(); throw 'Installed UI regression timed out' }
    Copy-Item "$target/ui-*" "$repo/ci-logs/" -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0) { throw "Installed UI regression failed: $($p.ExitCode)" }
    $report.Add('PASS installed RDP ActiveX, embedded SSH fixture, WebView2, VPN safeguards and inheritance suite')
} finally {
    Remove-Item Env:DESKPILOT_UI_SMOKE -ErrorAction SilentlyContinue
    if (Test-Path $profile) { Remove-Item $profile -Recurse -Force }
    Move-Item $backup $profile
}
$uninstall = Start-Process (Join-Path $target 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$repo/ci-logs/installer-uninstall.log`"") -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw 'Uninstall failed' }
Assert-Data
if (Test-Path $exe) { throw 'Uninstall retained application executable' }
if (Test-Path $link) { throw 'Uninstall retained Start Menu shortcut' }
$report.Add('PASS uninstall removes application and shortcuts while retaining DeskPilot, VPN and original Terminals data sentinels')
$report | Tee-Object (Join-Path $repo 'ci-logs/installer-smoke.txt')
