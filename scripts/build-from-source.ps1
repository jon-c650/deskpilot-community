$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
Push-Location $repo
try {
    if (-not $env:RUNNER_TEMP) { $env:RUNNER_TEMP = Join-Path ([IO.Path]::GetTempPath()) ('DeskPilot-build-' + [Guid]::NewGuid()); New-Item -ItemType Directory $env:RUNNER_TEMP | Out-Null }
    nuget restore packaging/packages.config -PackagesDirectory upstream/Terminals/Source/packages -ConfigFile packaging/NuGet.Config -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed' }
    Get-ChildItem upstream/Terminals/Source -Filter *.csproj -Recurse | ForEach-Object {
        $s = [IO.File]::ReadAllText($_.FullName)
        $updated = [regex]::Replace($s,'<TargetFrameworkVersion>v4\.0</TargetFrameworkVersion>','<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>')
        if ($updated -ne $s) {[IO.File]::WriteAllText($_.FullName,$updated)}
    }
    & ./scripts/prepare-session-dependencies.ps1
    & ./scripts/prepare-rdp-interop.ps1
    foreach ($name in @('Terminals','Terminals.Plugins.Rdp','Terminals.Plugins.Putty','Terminals.Plugins.Web')) {
        msbuild "upstream/Terminals/Source/$name/$name.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:RestorePackages=false /m /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
    }
    New-Item -ItemType Directory -Force ci-logs | Out-Null
    $output = 'upstream/Terminals/Build/Output/Release'
    $sdk = 'upstream/Terminals/Source/packages/Microsoft.Web.WebView2.1.0.4258.31'
    Copy-Item "$sdk/lib/net462/Microsoft.Web.WebView2.Core.dll","$sdk/lib/net462/Microsoft.Web.WebView2.WinForms.dll" $output
    Copy-Item "$sdk/runtimes" $output -Recurse -Force
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) {throw 'MSVC x86 build tools missing'}
    $dev = Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
    $batch = Join-Path $env:RUNNER_TEMP 'deskpilot-source-native.cmd'
    @('@echo off',('call "' + $dev + '" -arch=x86 -host_arch=x64'), 'if errorlevel 1 exit /b 1',('cd /d "' + $repo + '"'),'call helpers\WireGuardProfiles\build.cmd','exit /b %errorlevel%') | Set-Content -Encoding ascii $batch
    cmd /d /c $batch
    if ($LASTEXITCODE -ne 0) {throw 'Native helper build failed'}
    if (Test-Path BUILD.txt) {Copy-Item BUILD.txt "$output/BUILD.txt"}
    elseif ($env:GITHUB_SHA) {Set-Content -Encoding ascii "$output/BUILD.txt" $env:GITHUB_SHA}
    Copy-Item docs/licenses "$output/licenses" -Recurse -Force
    python ./scripts/audit-release.py $output
    if ($LASTEXITCODE -ne 0) {throw 'Rebuilt payload provenance check failed'}
    Write-Host "Built source archive successfully: $output"
} finally {Pop-Location}
