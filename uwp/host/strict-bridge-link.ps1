param(
    [ValidateSet('x86', 'x64', 'arm64', 'arm')][string]$Architecture = 'x64',
    [Parameter(Mandatory)][string]$RustLibrary,
    [Parameter(Mandatory)][string]$HostKit,
    [Parameter(Mandatory)][string]$MsvcToolsetRoot,
    [Parameter(Mandatory)][string]$SdkRoot,
    [Parameter(Mandatory)][string]$RustLld,
    [Parameter(Mandatory)][string]$SentinelObject,
    [Parameter(Mandatory)][string]$Output,
    [switch]$NativeMod
)

$ErrorActionPreference = 'Stop'
$machine = @{ x86 = 'X86'; x64 = 'X64'; arm64 = 'ARM64'; arm = 'ARM' }[$Architecture]
$sdkArchitecture = @{ x86 = 'x86'; x64 = 'x64'; arm64 = 'arm64'; arm = 'arm' }[$Architecture]
$hostKit = [IO.Path]::GetFullPath($HostKit)
$msvc = [IO.Path]::GetFullPath($MsvcToolsetRoot)
$sdk = [IO.Path]::GetFullPath($SdkRoot)
$rust = [IO.Path]::GetFullPath($RustLibrary)
$linker = [IO.Path]::GetFullPath($RustLld)
$sentinel = [IO.Path]::GetFullPath($SentinelObject)
$outputPath = [IO.Path]::GetFullPath($Output)
$outputDirectory = [IO.Path]::GetDirectoryName($outputPath)
$hostImport = Join-Path $hostKit 'HorizonUWPHost.lib'
$tlsObject = Join-Path $hostKit 'link\tls_index.obj'
$targetMsvcRuntime = Join-Path $msvc "lib\$sdkArchitecture\store"
$hostMsvcRuntime = Join-Path $msvc 'lib\x64'
$bridgeExports = if ($NativeMod) {
    Join-Path $PSScriptRoot '..\native-mod-probe\packaged-native-exports.def'
} else {
    Join-Path $PSScriptRoot 'game-bridge-exports.def'
}
$runtime = @('libcmt.lib', 'tls_index.obj')

foreach ($path in @($rust, $linker, $hostImport, $bridgeExports, $sentinel,
        (Join-Path $msvc 'include\vcruntime.h'),
        (Join-Path $hostMsvcRuntime 'msvcrt.lib'), (Join-Path $hostMsvcRuntime 'vcruntime.lib'),
        (Join-Path $targetMsvcRuntime 'msvcrt.lib'), (Join-Path $targetMsvcRuntime 'vcruntime.lib'),
        (Join-Path $sdk "Lib\10.0.19041.0\ucrt\$sdkArchitecture\ucrt.lib"),
        (Join-Path $sdk "Lib\10.0.19041.0\um\$sdkArchitecture\WindowsApp.lib"),
    (Join-Path $sdk "Lib\10.0.19041.0\um\$sdkArchitecture\iphlpapi.lib"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required UWP link input is missing: $path" }
}
foreach ($name in $runtime) {
    $path = Join-Path $hostKit "link\$name"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The verified client release is missing its compact runtime link input: $name" }
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$arguments = @(
    '-flavor', 'link', '/NOLOGO', '/DLL', '/NOENTRY', '/APPCONTAINER', "/MACHINE:$machine", '/OPT:REF',
    '/NODEFAULTLIB:kernel32.lib', '/NODEFAULTLIB:libucrt.lib',
    $(if ($Architecture -eq 'x86') { '/INCLUDE:__tls_used' } else { '/INCLUDE:_tls_used' }),
    '/DEFAULTLIB:msvcrt', '/DEFAULTLIB:vcruntime', '/DEFAULTLIB:ucrt',
    "/LIBPATH:$hostKit\link",
    "/LIBPATH:$targetMsvcRuntime",
    "/LIBPATH:$sdk\Lib\10.0.19041.0\ucrt\$sdkArchitecture",
    "/LIBPATH:$sdk\Lib\10.0.19041.0\um\$sdkArchitecture",
    "/OUT:$outputPath", "/IMPLIB:$outputPath.lib", "/MAP:$outputPath.map", "/DEF:$bridgeExports",
    $tlsObject, $rust,
    $hostImport,
    $sentinel,
    'WindowsApp.lib', 'ntdll.lib', 'd3d11.lib', 'd3d12.lib', 'dxgi.lib', 'xaudio2.lib', 'iphlpapi.lib'
)
& $linker @arguments
if ($LASTEXITCODE -ne 0) { throw "Rust LLD failed to link the prebuilt host bridge (exit code $LASTEXITCODE)." }
if (Select-String -LiteralPath "$outputPath.map" -Pattern 'dead-import-sentinels\.obj|forbidden_api' -Quiet) {
    Remove-Item -LiteralPath $outputPath -Force
    throw 'The game bridge uses an API that is unavailable to packaged applications.'
}

$stream = [IO.File]::Open($outputPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
    [IO.FileShare]::Read -bor [IO.FileShare]::Delete)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'The linked game bridge is not a PE file.' }
    $stream.Position = 0x3c
    $pe = $reader.ReadInt32()
    if ($pe -lt 0 -or $pe + 6 -gt $stream.Length) { throw 'The linked game bridge has an invalid PE header.' }
    $stream.Position = $pe
    if ($reader.ReadUInt32() -ne 0x4550) { throw 'The linked game bridge has an invalid PE signature.' }
    $actual = $reader.ReadUInt16()
    $sectionCount = $reader.ReadUInt16()
    $stream.Position = $pe + 20
    $optionalHeaderSize = $reader.ReadUInt16()
    $expected = @{ X86 = 0x014c; X64 = 0x8664; ARM = 0x01c4; ARM64 = 0xaa64 }[$machine]
    if ($actual -ne $expected) { throw "The game bridge architecture is incorrect for $Architecture." }

    # Validate DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS]. Rust's thread-local
    # runtime needs this directory even when the bridge has no native mods.
    $optionalHeader = $pe + 24
    $stream.Position = $optionalHeader
    $magic = $reader.ReadUInt16()
    if ($magic -eq 0x10b) {
        $directoryCountOffset = $optionalHeader + 92
        $directoriesOffset = $optionalHeader + 96
        $expectedTlsSize = 24
        $stream.Position = $optionalHeader + 28
        $imageBase = [uint64]$reader.ReadUInt32()
    } elseif ($magic -eq 0x20b) {
        $directoryCountOffset = $optionalHeader + 108
        $directoriesOffset = $optionalHeader + 112
        $expectedTlsSize = 40
        $stream.Position = $optionalHeader + 24
        $imageBase = $reader.ReadUInt64()
    } else {
        throw 'The linked game bridge has an unsupported PE optional-header format.'
    }
    if ($directoriesOffset + (10 * 8) -gt $optionalHeader + $optionalHeaderSize -or
        $optionalHeader + $optionalHeaderSize -gt $stream.Length) {
        throw 'The linked game bridge has a truncated PE optional header.'
    }
    $stream.Position = $directoryCountOffset
    $directoryCount = $reader.ReadUInt32()
    if ($directoryCount -le 9) { throw 'The game bridge has no PE TLS data directory.' }
    $stream.Position = $directoriesOffset + (9 * 8)
    $tlsRva = $reader.ReadUInt32()
    $tlsSize = $reader.ReadUInt32()
    if ($tlsRva -eq 0 -or $tlsSize -ne $expectedTlsSize) {
        throw 'The game bridge is missing the PE TLS directory required by Rust thread-local storage.'
    }

    $stream.Position = $optionalHeader + 56
    $imageSize = [uint64]$reader.ReadUInt32()
    $sections = $optionalHeader + $optionalHeaderSize
    if ($sections + 40 * $sectionCount -gt $stream.Length) {
        throw 'The linked game bridge has a truncated PE section table.'
    }
    $tlsOffset = $null
    $tlsTemplate = $null
    for ($section = 0; $section -lt $sectionCount; $section++) {
        $stream.Position = $sections + $section * 40
        $sectionName = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)
        $sectionSize = $reader.ReadUInt32()
        $sectionRva = $reader.ReadUInt32()
        $rawSize = $reader.ReadUInt32()
        $rawStart = $reader.ReadUInt32()
        if ($sectionName -ceq '.tls') {
            $tlsTemplate = [pscustomobject]@{ Rva = $sectionRva; Size = $sectionSize }
        }
        $offset = [long]$tlsRva - $sectionRva
        if ($offset -ge 0 -and $offset + $tlsSize -le $rawSize) {
            $tlsOffset = $rawStart + $offset
        }
    }
    if ($null -eq $tlsOffset -or $tlsOffset + $tlsSize -gt $stream.Length) {
        throw 'The game bridge TLS directory is outside the file-backed sections.'
    }
    $stream.Position = $tlsOffset
    $addresses = for ($field = 0; $field -lt 4; $field++) {
        if ($magic -eq 0x20b) { $reader.ReadUInt64() } else { [uint64]$reader.ReadUInt32() }
    }
    $start, $end, $index, $callbacks = $addresses
    $imageEnd = $imageBase + $imageSize
    $pointerSize = if ($magic -eq 0x20b) { 8 } else { 4 }
    if ($start -lt $imageBase -or $start -ge $imageEnd -or
        $end -lt $start -or $end -gt $imageEnd -or
        $index -lt $imageBase -or $index -gt $imageEnd - 4 -or
        ($callbacks -ne 0 -and ($callbacks -lt $imageBase -or $callbacks -gt $imageEnd - $pointerSize))) {
        throw 'The linked game bridge has invalid TLS addresses for its PE pointer width.'
    }
    if (-not $tlsTemplate -or $start -ne $imageBase + $tlsTemplate.Rva -or
        $end -gt $start + $tlsTemplate.Size -or
        ($tlsRva -ge $tlsTemplate.Rva -and $tlsRva -lt $tlsTemplate.Rva + $tlsTemplate.Size)) {
        throw 'The game bridge TLS template does not match section-relative Rust TLS offsets.'
    }
} catch {
    Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
    throw
} finally { $reader.Dispose() }
if (Select-String -LiteralPath "$outputPath.map" -Pattern 'atexit:api-ms-win-crt-runtime-l1-1-0\.dll' -Quiet) {
    Remove-Item -LiteralPath $outputPath -Force
    throw 'The linked game bridge imports the unavailable UCRT atexit entry point.'
}
$kind = if ($NativeMod) { 'native mod' } else { 'bridge' }
Write-Host "Strict UWP $kind link succeeded for $Architecture."
