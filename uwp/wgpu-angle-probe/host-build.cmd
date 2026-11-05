@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 store 10.0.15063.0
if errorlevel 1 exit /b %errorlevel%
pushd "%~dp0"
cl /nologo /EHsc /ZW /std:c++17 /O2 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /AI"%WindowsSdkDir%UnionMetadata\10.0.15063.0" /I"%~dp0." "%~dp0..\host\App.cpp" "%~dp0Dx11Renderer.cpp" /link /APPCONTAINER /SUBSYSTEM:WINDOWS /MACHINE:X64 "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.15063.0\um\x64\kernel32.lib" WindowsApp.lib userenv.lib ws2_32.lib bcrypt.lib ntdll.lib "%~dp0target\x86_64-uwp-windows-msvc\debug\wgpu_uwp_probe.lib" /OUT:WgpuProbe.exe
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%


