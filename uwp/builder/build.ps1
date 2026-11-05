param(
    [ValidateSet("Debug", "Release")][string] $Configuration = "Release",
    [switch] $Publish
)
$ErrorActionPreference = "Stop"
$roots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ } | Select-Object -Unique
$tools = foreach ($root in $roots) {
    $base = Join-Path $root "Microsoft Visual Studio"
    if (Test-Path -LiteralPath $base) {
        Get-ChildItem -LiteralPath $base -Directory | ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                $msbuild = Join-Path $_.FullName "MSBuild\Microsoft\VisualStudio"
                if (Test-Path -LiteralPath $msbuild) {
                    Get-ChildItem -LiteralPath $msbuild -Directory | ForEach-Object {
                        $candidate = Join-Path $_.FullName "AppxPackage"
                        if ((Test-Path -LiteralPath (Join-Path $candidate "Microsoft.Build.Packaging.Pri.Tasks.dll")) -and
                            (Test-Path -LiteralPath (Join-Path $candidate "Microsoft.Build.AppxPackage.dll"))) { $candidate }
                    }
                }
            }
        }
    }
}
$tools = $tools | Select-Object -First 1
if (-not $tools) {
    throw "Visual Studio AppxPackage build tasks were not found in standard locations. Install the Windows application development build tools, or pass AppxMSBuildToolsPath to dotnet build for a custom installation."
}
$project = Join-Path $PSScriptRoot "App\Horizon.Builder.App.csproj"
if ($Publish) {
    dotnet publish $project -c $Configuration -p:Platform=x64 -p:SelfContained=true "-p:AppxMSBuildToolsPath=$tools/" -m
} else {
    dotnet build $project -c $Configuration -p:Platform=x64 "-p:AppxMSBuildToolsPath=$tools/" -m
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
