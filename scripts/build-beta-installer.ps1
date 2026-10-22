param([Parameter(Mandatory)][string]$PayloadDir, [Parameter(Mandatory)][string]$SourceArchive, [switch]$PublicRelease)
$ErrorActionPreference = 'Stop'
if ($PublicRelease) { & "$PSScriptRoot/assert-public-release.ps1" }
$repo = Split-Path $PSScriptRoot
$payload = (Resolve-Path $PayloadDir).Path
$source = (Resolve-Path $SourceArchive).Path
python (Join-Path $PSScriptRoot 'audit-release.py') $payload
if ($LASTEXITCODE -ne 0) {throw 'Release provenance audit failed'}
foreach ($file in @('DeskPilot.Preview.exe','DeskPilot.WireGuardHelper.exe','BUILD.txt','Plugins/Rdp/Terminals.Plugins.Rdp.dll','Plugins/Putty/Terminals.Plugins.Putty.dll','Plugins/Web/Terminals.Plugins.Web.dll')) {
    if (-not (Test-Path (Join-Path $payload $file))) { throw "Required core file missing: $file" }
}
$forbidden = @('Metro.dll','TransPort2006.dll','TransPort2006.Design.dll','ZedGraph.dll','SharpPcap.dll','wpcap.dll','HexEditor.dll','AWSSDK.dll','FlickrNet.dll','mstscax.dll','mstsc.exe')
Get-ChildItem $payload -Recurse -File | ForEach-Object {
    if ($_.Name -in $forbidden) { throw "Excluded legacy/OS binary in installer payload: $($_.Name)" }
}
$build = (Get-Content (Join-Path $payload 'BUILD.txt') -Raw).Trim()
if ($build -notmatch '^[a-fA-F0-9]{40}$') { throw 'Invalid build identity' }
python (Join-Path $PSScriptRoot 'verify-source.py') $source (Join-Path $payload 'BUILD.txt')
if ($LASTEXITCODE -ne 0) {throw 'Source archive verification failed'}
$toolDir = Join-Path $env:RUNNER_TEMP 'deskpilot-inno-6.7.3'
$setup = Join-Path $env:RUNNER_TEMP 'deskpilot-inno-6.7.3.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $setup
if ((Get-FileHash $setup -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') { throw 'Inno Setup checksum mismatch' }
$install = Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$toolDir`"") -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installer compiler setup failed: $($install.ExitCode)" }
$arguments = @("/DPayloadDir=$payload", "/DBuildId=$($build.Substring(0,8))", "/DSourceArchive=$source")
if ($PublicRelease) {$arguments += '/DPublicBeta'}
$arguments += (Join-Path $repo 'packaging/DeskPilot-Beta.iss')
& (Join-Path $toolDir 'ISCC.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }

$output = Join-Path $repo 'installer-output'
Copy-Item (Join-Path $repo $(if ($PublicRelease) {'packaging/PUBLIC-BETA.txt'} else {'packaging/PRIVATE-TEST.txt'})) $output
Copy-Item $source $output
Copy-Item (Join-Path $repo 'docs/release/LICENSE-REVIEW.md') $output
Copy-Item (Join-Path $repo 'docs/release/INSTALLER-TESTING.md') $output
Copy-Item (Join-Path $repo 'docs/release/THIRD-PARTY-NOTICES.md') $output
Copy-Item (Join-Path $repo 'upstream/Terminals/LICENSE.md') $output
Copy-Item (Join-Path $payload 'BUILD.txt') $output
Get-ChildItem $output -File | Where-Object {$_.Extension -in '.exe','.zip'} | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.Name)" } | Set-Content (Join-Path $output 'SHA256SUMS.txt')
