@echo off
setlocal
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Community"
call "%VSROOT%\VC\Auxiliary\Build\vcvarsall.bat" x64 store 10.0.15063.0
if errorlevel 1 exit /b %errorlevel%
if not exist "%~dp0build" mkdir "%~dp0build"
pushd "%~dp0build"
cl /nologo /c /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\10.0.15063.0" /I"%~dp0." "%~dp0..\..\host\App.cpp" "%~dp0Dx11Renderer.cpp"
if errorlevel 1 (popd & exit /b 1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0link-host.ps1"
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
