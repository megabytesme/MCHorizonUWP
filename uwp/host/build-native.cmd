@echo off
setlocal
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
set "ARCH=%~1"
if "%ARCH%"=="" set "ARCH=x64"
set "VCARCH=x64"
set "MACHINE=X64"
set "OUTDIR=%~dp0build"
set "SDK=10.0.15063.0"
if /i "%ARCH%"=="arm64" (
    set "VCARCH=x64_arm64"
    set "MACHINE=ARM64"
    set "OUTDIR=%~dp0build\arm64"
    set "SDK=10.0.26100.0"
)
if /i "%ARCH%"=="arm" (
    set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2017\Community"
    set "VCARCH=x64_arm"
    set "MACHINE=ARM"
    set "OUTDIR=%~dp0build\arm"
)
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="arm64" if /i not "%ARCH%"=="arm" exit /b 1
if not exist "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" exit /b 1
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% store %SDK%
if errorlevel 1 exit /b %errorlevel%
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
set "FXC=%ProgramFiles(x86)%\Windows Kits\10\bin\%SDK%\x64\fxc.exe"
if not exist "%FXC%" exit /b 1
"%FXC%" /nologo /T vs_4_0_level_9_1 /E VSMain /Vn g_phone_probe_vs /Fh "%OUTDIR%\PhoneProbeVS.h" "%~dp0..\dx11\PhoneProbe.hlsl"
if errorlevel 1 exit /b 1
"%FXC%" /nologo /T ps_4_0_level_9_1 /E PSMain /Vn g_phone_probe_ps /Fh "%OUTDIR%\PhoneProbePS.h" "%~dp0..\dx11\PhoneProbe.hlsl"
if errorlevel 1 exit /b 1
set "BRIDGE_DEFINE="
set "BRIDGE_LINK="
set "GAME_DEFINE="
set "GAME_LINK="
set "GAME_SOURCE="
if defined HORIZON_UWP_BRIDGE_LIB (
    if not exist "%HORIZON_UWP_BRIDGE_LIB%" exit /b 1
    set "BRIDGE_DEFINE=/DHORIZON_UWP_CORE_BRIDGE"
    set BRIDGE_LINK="%HORIZON_UWP_BRIDGE_LIB%"
)
if defined HORIZON_UWP_GAME_LIB (
    if not exist "%HORIZON_UWP_GAME_LIB%" exit /b 1
    set "GAME_DEFINE=/DHORIZON_UWP_GAME_BRIDGE"
    set GAME_LINK="%HORIZON_UWP_GAME_LIB%"
    set GAME_SOURCE="%~dp0..\window-bridge\CoreWindowEvents.cpp"
)
pushd "%OUTDIR%"
cl /nologo /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 %BRIDGE_DEFINE% %GAME_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" /I"%~dp0..\dx11" /I"%OUTDIR%" "%~dp0App.cpp" "%~dp0..\dx11\Dx11Renderer.cpp" %GAME_SOURCE% /link /APPCONTAINER /SUBSYSTEM:WINDOWS /MACHINE:%MACHINE% WindowsApp.lib d3d11.lib dxgi.lib %BRIDGE_LINK% %GAME_LINK% /OUT:HorizonUWP.exe
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%

