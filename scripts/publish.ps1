#!/usr/bin/env pwsh
# Builds the Windows release: publish/ClearanceLink/ (ready to run, no .NET install needed) and
# publish/ClearanceLink-<version>-win-x64.zip. Usage: ./scripts/publish.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$out = 'publish/ClearanceLink'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none', '-o', $out)
dotnet publish src/ClearanceLink.Host @common
if ($LASTEXITCODE) { exit $LASTEXITCODE }
# The stub does nothing, so trim it hard; it only has to exist as FlightSimulator2024.exe.
dotnet publish src/ClearanceLink.SimStub @common -p:PublishTrimmed=true
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Remove-Item "$out/*.runtimeconfig.json", "$out/*.deps.json" -ErrorAction SilentlyContinue
Copy-Item mapping "$out/mapping" -Recurse
Copy-Item packaging/* $out -Recurse
Copy-Item LICENSE "$out/LICENSE.txt"

# The X-Plane traffic plugin needs CMake and the MSVC C++ tools. GitHub Actions (release workflow) has both;
# without them the zip is built without the plugin and BeyondATC's traffic is not drawn.
if ((Get-Command cmake -ErrorAction SilentlyContinue) -and -not $env:CLEARANCELINK_SKIP_PLUGIN) {
    cmake -S plugin -B build/plugin -A x64
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    cmake --build build/plugin --config Release --parallel
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    New-Item -ItemType Directory "$out/X-Plane plugin" -Force | Out-Null
    Copy-Item build/plugin/ClearanceLink "$out/X-Plane plugin/ClearanceLink" -Recurse
} else {
    Write-Warning 'cmake not found: building the zip without the X-Plane traffic plugin'
}

$version = (Get-Item "$out/ClearanceLink.exe").VersionInfo.ProductVersion.Split('+')[0]
$zip = "publish/ClearanceLink-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out/*" -DestinationPath $zip
Write-Host "Built $zip"
