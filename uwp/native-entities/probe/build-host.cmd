@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 store 10.0.15063.0
if errorlevel 1 exit /b 1
if not exist "%~dp0build" mkdir "%~dp0build"
pushd "%~dp0build"
cl /nologo /c /EHsc /ZW /std:c++17 /O2 /Gy /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /AI"%WindowsSdkDir%UnionMetadata\10.0.15063.0" "%~dp0App.cpp"
if errorlevel 1 (popd & exit /b 1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0link.ps1"
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
