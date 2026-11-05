@echo off
setlocal
if not defined HORIZON_D3D11_PROBE_LIB exit /b 1
if not exist "%HORIZON_D3D11_PROBE_LIB%" exit /b 1
set "ARCH=%~1"
if "%ARCH%"=="" set "ARCH=x64"
set "VCARCH=x64"
set "PROBE_MACHINE=X64"
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
set "OUTDIR=%~dp0build"
set "DIALOG_DEFINE="
if /i "%ARCH%"=="arm" (
    set "VCARCH=x64_arm"
    set "PROBE_MACHINE=ARM"
    set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2017\Community"
    set "OUTDIR=%~dp0build\arm"
    set "DIALOG_DEFINE=/DHORIZON_D3D11_PHONE_DIALOG"
)
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="arm" exit /b 1
set "SDK=10.0.15063.0"
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% store %SDK%
if errorlevel 1 exit /b 1
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
pushd "%OUTDIR%"
cl /nologo /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP %DIALOG_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0App.cpp"
if errorlevel 1 (popd & exit /b 1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0link-host.ps1"
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
