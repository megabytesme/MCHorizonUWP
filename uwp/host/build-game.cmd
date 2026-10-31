@echo off
setlocal
set "ARCH=%~1"
if "%ARCH%"=="" set "ARCH=x64"
set "VCARCH=x64"
set "GAME_MACHINE=X64"
set "SDK=10.0.15063.0"
if /i "%ARCH%"=="arm64" (
    set "VCARCH=x64_arm64"
    set "GAME_MACHINE=ARM64"
    set "SDK=10.0.26100.0"
)
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="arm64" exit /b 1
if not defined HORIZON_UWP_GAME_LIB (
    echo Set HORIZON_UWP_GAME_LIB to the Rust game bridge static library.
    exit /b 1
)
if not exist "%HORIZON_UWP_GAME_LIB%" exit /b 1
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% store %SDK%
if errorlevel 1 exit /b %errorlevel%
set "OUTDIR=%~dp0build\game-%ARCH%"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
set "FRAME_DEFINE="
if defined HORIZON_UWP_FRAME_PROBE set "FRAME_DEFINE=/DHORIZON_UWP_FRAME_PROBE"
pushd "%OUTDIR%"
cl /nologo /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /DHORIZON_UWP_GAME_BRIDGE %FRAME_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" /I"%~dp0..\dx11" "%~dp0App.cpp" "%~dp0..\dx11\Dx11Renderer.cpp" "%~dp0..\window-bridge\CoreWindowEvents.cpp"
if errorlevel 1 (popd & exit /b 1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0strict-game-link.ps1"
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
