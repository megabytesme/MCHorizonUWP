@echo off
setlocal
set "ARCH=%~1"
set "OUTPUT=%~2"
if "%OUTPUT%"=="" exit /b 1
set "VCARCH=x64"
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
set "SDK=10.0.26100.0"
set "WINUI_VERSION=2.8.7"
if /i "%ARCH%"=="arm" (
    set "VCARCH=x64_arm"
    set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2017\Community"
    set "SDK=10.0.16299.0"
    set "WINUI_VERSION=2.7.3"
)
if /i not "%ARCH%"=="arm" if /i not "%ARCH%"=="x64" exit /b 1
set "WINUI=%USERPROFILE%\.nuget\packages\microsoft.ui.xaml\%WINUI_VERSION%\lib\uap10.0"
if not exist "%WINUI%\Microsoft.UI.Xaml.winmd" (
    echo WinUI %WINUI_VERSION% is missing. Restore Microsoft.UI.Xaml %WINUI_VERSION% and review its license before building the XAML shell.
    exit /b 1
)
set "PROBE_DEFINE="
if defined HORIZON_UWP_SHELL_PROBE set "PROBE_DEFINE=/DHORIZON_UWP_SHELL_PROBE"
if defined HORIZON_UWP_NATIVE_D3D11 set "PROBE_DEFINE=%PROBE_DEFINE% /DHORIZON_UWP_NATIVE_D3D11"
if defined HORIZON_UWP_DX12 set "PROBE_DEFINE=%PROBE_DEFINE% /DHORIZON_UWP_DX12"
rem The caller may already be in a different Visual Studio environment. Reset
rem only this child process so the ARM32 compiler and 16299 SDK can be selected.
set "VSCMD_VER="
set "VSCMD_ARG_TGT_ARCH="
set "VSCMD_ARG_HOST_ARCH="
set "VSCMD_ARG_APP_PLAT="
set "VSINSTALLDIR="
set "VCINSTALLDIR="
set "VCToolsInstallDir="
set "VCToolsVersion="
set "WindowsSdkDir="
set "WindowsSDKVersion="
set "INCLUDE="
set "LIB="
set "LIBPATH="
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% store %SDK%
if errorlevel 1 exit /b 1
cl /nologo /MP /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 %PROBE_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" /AI"%WINUI%" /Fo"%OUTPUT%\XamlShell.obj" "%~dp0XamlShell.cpp"
exit /b %errorlevel%
