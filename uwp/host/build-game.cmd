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
if /i "%ARCH%"=="arm" (
    set "VCARCH=x64_arm"
    set "GAME_MACHINE=ARM"
    set "SDK=10.0.15063.0"
)
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="arm64" if /i not "%ARCH%"=="arm" exit /b 1
if defined HORIZON_UWP_DX12 if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="arm64" exit /b 1

if not defined HORIZON_UWP_GAME_LIB (
    echo Set HORIZON_UWP_GAME_LIB to the Rust game bridge static library.
    exit /b 1
)
if defined HORIZON_UWP_XAML_SHELL if not defined HORIZON_UWP_ACCOUNT (
    echo The XAML shell requires HORIZON_UWP_ACCOUNT.
    exit /b 1
)
if not exist "%HORIZON_UWP_GAME_LIB%" exit /b 1
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
if /i "%ARCH%"=="arm" set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2017\Community"
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% store %SDK%
if errorlevel 1 exit /b %errorlevel%
set "OUTDIR=%~dp0build\game-%ARCH%"
if defined HORIZON_UWP_BUILD_DIR set "OUTDIR=%HORIZON_UWP_BUILD_DIR%"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
set "FXC=%ProgramFiles(x86)%\Windows Kits\10\bin\%SDK%\x64\fxc.exe"
if not exist "%FXC%" exit /b 1
"%FXC%" /nologo /T vs_4_0_level_9_1 /E VSMain /Vn g_phone_probe_vs /Fh "%OUTDIR%\PhoneProbeVS.h" "%~dp0..\dx11\PhoneProbe.hlsl"
if errorlevel 1 exit /b 1
"%FXC%" /nologo /T ps_4_0_level_9_1 /E PSMain /Vn g_phone_probe_ps /Fh "%OUTDIR%\PhoneProbePS.h" "%~dp0..\dx11\PhoneProbe.hlsl"
if errorlevel 1 exit /b 1
set "FRAME_DEFINE="
if defined HORIZON_UWP_FRAME_PROBE set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_FRAME_PROBE"
if defined HORIZON_UWP_DISPLAY_DIAGNOSTICS set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_DISPLAY_DIAGNOSTICS"
if defined HORIZON_UWP_ACCOUNT set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_ACCOUNT"
if defined HORIZON_UWP_XAML_SHELL set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_XAML_SHELL"
if defined HORIZON_UWP_NATIVE_D3D11 set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_NATIVE_D3D11"
if defined HORIZON_UWP_ANGLE set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_ANGLE"
if defined HORIZON_UWP_TOUCH_MOD set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_TOUCH_MOD"
if defined HORIZON_UWP_DX12 set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_DX12"
if /i "%HORIZON_UWP_DEFAULT_RENDERER%"=="angle" set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_DEFAULT_RENDERER_ANGLE"
if /i "%HORIZON_UWP_DEFAULT_RENDERER%"=="dx12" set "FRAME_DEFINE=%FRAME_DEFINE% /DHORIZON_UWP_DEFAULT_RENDERER_DX12"
pushd "%OUTDIR%"
cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /DHORIZON_UWP_GAME_BRIDGE %FRAME_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" /I"%~dp0..\dx11" /I"%OUTDIR%" "%~dp0App.cpp" "%~dp0..\dx11\Dx11Renderer.cpp" "%~dp0..\window-bridge\CoreWindowEvents.cpp"
if errorlevel 1 (popd & exit /b 1)
if defined HORIZON_UWP_XAUDIO (
    cl /nologo /MP /c /EHsc /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 "%~dp0..\xaudio-stream\host\XAudioStream.cpp"
    if errorlevel 1 (popd & exit /b 1)
)
if defined HORIZON_UWP_HTTP (
    cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\https-bridge\host\HttpBridge.cpp"
    if errorlevel 1 (popd & exit /b 1)
)
if defined HORIZON_UWP_GAMEPAD (
    cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\gamepad-bridge\host\Gamepad.cpp"
    if errorlevel 1 (popd & exit /b 1)
)
if defined HORIZON_UWP_TOUCH_MOD (
    cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\gyro-bridge\host\Gyrometer.cpp"
    if errorlevel 1 (popd & exit /b 1)
)
cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\network-bridge\host\Srv.cpp"
if errorlevel 1 (popd & exit /b 1)
cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\picker\host\Picker.cpp"
if errorlevel 1 (popd & exit /b 1)
if defined HORIZON_UWP_DEVICE_VIBRATION (
    cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\device-vibration-bridge\host\DeviceVibration.cpp"
    if errorlevel 1 (popd & exit /b 1)
)
if defined HORIZON_UWP_ACCOUNT (
    cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\credential-store\host\CredentialStore.cpp"
    if errorlevel 1 (popd & exit /b 1)
    if defined HORIZON_UWP_XAML_SHELL (
        cmd /d /c ""%~dp0..\pregame\host\build-shell.cmd" "%ARCH%" "%OUTDIR%""
    ) else (
        cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0..\pregame\host\NativeDialog.cpp"
    )
    if errorlevel 1 (popd & exit /b 1)
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0strict-game-link.ps1"
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
