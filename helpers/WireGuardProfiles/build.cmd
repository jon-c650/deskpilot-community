@echo off
setlocal
if "%VSINSTALLDIR%"=="" exit /b 1
set "DP_OUT=upstream\Terminals\Build\Output\Release"
if not exist "%DP_OUT%" mkdir "%DP_OUT%"
rc /nologo /foci-logs\WireGuardHelper.res helpers\WireGuardProfiles\helper.rc
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /MT /W4 /GS /guard:cf /Fo"ci-logs\WireGuardHelper.obj" /Fe"%DP_OUT%\DeskPilot.WireGuardHelper.exe" helpers\WireGuardProfiles\helper.cpp ci-logs\WireGuardHelper.res /link /DELAYLOAD:advapi32.dll /DELAYLOAD:crypt32.dll /DELAYLOAD:shell32.dll /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /MANIFEST:NO
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /MT /W4 /GS /guard:cf /DPROFILE_HELPER_TESTS /Fo"ci-logs\WireGuardHelperTests.obj" /Fe"%TEMP%\DeskPilot.WireGuardHelperTests.exe" helpers\WireGuardProfiles\helper.cpp /link /DELAYLOAD:advapi32.dll /DELAYLOAD:crypt32.dll /DELAYLOAD:shell32.dll /SUBSYSTEM:CONSOLE /DYNAMICBASE /NXCOMPAT
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /MT /W4 /GS /guard:cf /DPROFILE_HELPER_PIPE_TESTS /Fo"ci-logs\WireGuardPipeTests.obj" /Fe"%TEMP%\DeskPilot.WireGuardPipeTests.exe" helpers\WireGuardProfiles\helper.cpp /link /DELAYLOAD:advapi32.dll /DELAYLOAD:crypt32.dll /DELAYLOAD:shell32.dll /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT
if errorlevel 1 exit /b 1
"%TEMP%\DeskPilot.WireGuardHelperTests.exe"
if errorlevel 1 exit /b 1
echo PASS native helper name and configuration validation; command hooks rejected; no services or profiles changed
exit /b 0
