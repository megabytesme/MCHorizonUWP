param([ValidateSet('x64', 'arm64', 'arm')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$hostDir = $PSScriptRoot
$build = Join-Path $hostDir 'build'
if ($Architecture -ne 'x64') { $build = Join-Path $build $Architecture }
$stage = Join-Path $build 'package'
New-Item -ItemType Directory -Force -Path $stage,(Join-Path $stage 'Assets') | Out-Null
Copy-Item (Join-Path $build 'HorizonUWP.exe') $stage -Force
$manifest = (Get-Content (Join-Path $hostDir 'AppxManifest.xml') -Raw).Replace('ProcessorArchitecture="x64"', "ProcessorArchitecture=`"$Architecture`"")
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8
Add-Type -AssemblyName System.Drawing
foreach ($logo in @(@('StoreLogo.png',50), @('Square150x150Logo.png',150), @('Square44x44Logo.png',44))) {
    $bitmap = [System.Drawing.Bitmap]::new($logo[1], $logo[1])
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(20, 36, 58))
    $bitmap.Save((Join-Path $stage (Join-Path 'Assets' $logo[0])), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
$makeAppx = '${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
$makeAppx = [Environment]::ExpandEnvironmentVariables($makeAppx.Replace('${env:ProgramFiles(x86)}', '%ProgramFiles(x86)%'))
$package = Join-Path $build "HorizonUWP-$Architecture.appx"
& $makeAppx pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed ($LASTEXITCODE)" }
Write-Host "Unsigned package: $package"
