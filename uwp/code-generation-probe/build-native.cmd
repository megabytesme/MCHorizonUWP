@echo off
setlocal
set "ARCH=%~1"
set "OUTDIR=%~2"
set "SDK=%~3"
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
if /i "%ARCH%"=="x64" set "VCARCH=x64"
if /i "%ARCH%"=="arm64" set "VCARCH=x64_arm64"
if /i "%ARCH%"=="arm" (
    set "VCARCH=x64_arm"
    set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
)
if not defined VCARCH exit /b 1
if defined HORIZON_UWP_VSROOT set "VSROOT=%HORIZON_UWP_VSROOT%"
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" %VCARCH% store %SDK%
if errorlevel 1 exit /b 1
pushd "%OUTDIR%"
if errorlevel 1 exit /b 1
set "PROBE_DEFINE="
set "PROBE_IMPORT="
if not "%~4"=="" (
    set "PROBE_DEFINE=/DHORIZON_UWP_WASMTIME_PROBE"
    set PROBE_IMPORT="%~4"
)
cl /nologo /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 %PROBE_DEFINE% /AI"%WindowsSdkDir%UnionMetadata\%SDK%" "%~dp0App.cpp"
if errorlevel 1 exit /b 1
link /NOLOGO /APPCONTAINER /SUBSYSTEM:WINDOWS /NODEFAULTLIB:kernel32.lib /OPT:REF /MAP:probe.map /OUT:Probe.exe App.obj WindowsApp.lib %PROBE_IMPORT%
if errorlevel 1 exit /b 1
dumpbin /NOLOGO /IMPORTS Probe.exe > imports.txt
exit /b %ERRORLEVEL%
