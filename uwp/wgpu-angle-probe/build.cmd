@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
set RUSTC_BOOTSTRAP=1
set CARGO_TARGET_DIR=%~dp0target
cd /d "%~dp0"
cargo +stable build -Z build-std=std,panic_abort --target x86_64-uwp-windows-msvc

