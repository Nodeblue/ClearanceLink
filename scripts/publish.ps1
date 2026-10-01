#!/usr/bin/env pwsh
# Builds the Windows release: publish/XPConnect/ (ready to run, no .NET install needed) and
# publish/XPConnect-<version>-win-x64.zip. Usage: ./scripts/publish.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$out = 'publish/XPConnect'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', '-o', $out)
dotnet publish src/XPConnect.Host @common
if ($LASTEXITCODE) { exit $LASTEXITCODE }
# The stub does nothing, so trim it hard; it only has to exist as FlightSimulator2024.exe.
dotnet publish src/XPConnect.SimStub @common -p:PublishTrimmed=true
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Remove-Item "$out/*.runtimeconfig.json", "$out/*.deps.json" -ErrorAction SilentlyContinue
Copy-Item mapping "$out/mapping" -Recurse
Copy-Item packaging/* $out -Recurse

$version = (& "$out/XPConnect.exe" --version).Split(' ')[-1]
$zip = "publish/XPConnect-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out/*" -DestinationPath $zip
Write-Host "Built $zip"
