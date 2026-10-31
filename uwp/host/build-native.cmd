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
set "BRIDGE_DEFINE="
set "BRIDGE_LINK="
if defined HORIZON_UWP_BRIDGE_LIB (
    if not exist "%HORIZON_UWP_BRIDGE_LIB%" exit /b 1
    set "BRIDGE_DEFINE=/DHORIZON_UWP_CORE_BRIDGE"
    set BRIDGE_LINK="%HORIZON_UWP_BRIDGE_LIB%"
)
pushd "%OUTDIR%"
cl /nologo /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 %BRIDGE_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" /I"%~dp0..\dx11" "%~dp0App.cpp" "%~dp0..\dx11\Dx11Renderer.cpp" /link /APPCONTAINER /SUBSYSTEM:WINDOWS /MACHINE:%MACHINE% WindowsApp.lib d3d11.lib dxgi.lib %BRIDGE_LINK% /OUT:HorizonUWP.exe
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%

