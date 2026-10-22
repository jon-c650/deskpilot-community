# Generate only managed API wrappers from the build runner's installed Windows
# control. Never distribute mstscax.dll/mstsc.exe or replace/register OS files.
$ErrorActionPreference = 'Stop'
$tools = Get-ChildItem 'C:/Program Files (x86)/Microsoft SDKs/Windows' -Filter AxImp.exe -Recurse | Where-Object { $_.Directory.Name -ne 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $tools) { throw 'Windows SDK AxImp is required' }
$tlbimp = Join-Path $tools.Directory.FullName 'TlbImp.exe'
$control = Join-Path $env:WINDIR 'SysWOW64/mstscax.dll'
if (-not (Test-Path $control)) { throw 'Installed 32-bit Windows RDP control missing' }
$target = Join-Path $PSScriptRoot '../upstream/Terminals/Resources/Libraries/mstsc'
New-Item -ItemType Directory -Force $target | Out-Null
$scratch = Join-Path $env:RUNNER_TEMP ('deskpilot-rdp-interop-' + [Guid]::NewGuid())
New-Item -ItemType Directory $scratch | Out-Null
Push-Location $scratch
try {
    # One AxImp invocation generates matching RCW and ActiveX wrappers together.
    # A fresh output directory prevents reuse of any vendored assembly identity.
    & $tools.FullName $control /out:AxInterop.MSTSCLib.dll /source /silent
    if ($LASTEXITCODE -ne 0) { throw 'RDP wrapper generation failed' }
    if (-not (Test-Path 'MSTSCLib.dll')) { throw 'Generated RDP type-library wrapper missing' }
    Copy-Item '*.dll' $target -Force
    Copy-Item '*.cs' $target -Force
} finally {Pop-Location}
@{
    description='Managed interop generated from the installed Windows RDP API, not an OS implementation';
    os=[Environment]::OSVersion.VersionString;
    controlVersion=(Get-Item $control).VersionInfo.FileVersion;
    toolVersion=$tools.VersionInfo.FileVersion;
    sources=@(Get-ChildItem $scratch -Filter '*.cs' | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName).Hash} });
    wrappers=@(Get-ChildItem $target -Filter '*.dll' | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName).Hash} })
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $target 'GENERATION.json')
