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
Copy-Item LICENSE-EXCEPTION.txt $out

# Third-party notices that come with the packages inside ClearanceLink.exe (THIRD-PARTY-NOTICES.txt lists them all),
# taken from the exact package versions this publish used.
$assets = Get-Content src/ClearanceLink.Host/obj/project.assets.json -Raw | ConvertFrom-Json
$packages = @($assets.packageFolders.PSObject.Properties.Name)[0]
function Get-PackageDir([string]$id) {
    $lib = $assets.libraries.PSObject.Properties | Where-Object { $_.Name -like "$id/*" } | Select-Object -First 1
    if ($lib) { return Join-Path $packages $lib.Value.path }
    $dep = $assets.project.frameworks.'net10.0'.downloadDependencies | Where-Object { $_.name -eq $id } | Select-Object -First 1
    if ($dep) { return Join-Path $packages "$($id.ToLowerInvariant())/$(($dep.version -split '[\[\],\s]+' | Where-Object { $_ })[0])" }
    # A runtime pack that came with the SDK is not downloaded, so it is not listed; it sits in the SDK's packs folder.
    $pack = Get-ChildItem (Join-Path (Split-Path (Get-Command dotnet).Source) "packs/$id") -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
    if ($pack) { return $pack.FullName }
    throw "package $id not found in project.assets.json"
}
$licenses = New-Item -ItemType Directory "$out/licenses" -Force
$notices = [ordered]@{
    'Microsoft.NETCore.App.Runtime.win-x64' = @('THIRD-PARTY-NOTICES.TXT', 'dotnet-THIRD-PARTY-NOTICES.txt')
    'SkiaSharp.NativeAssets.Win32'          = @('THIRD-PARTY-NOTICES.txt', 'SkiaSharp-THIRD-PARTY-NOTICES.txt')
    'CommunityToolkit.HighPerformance'      = @('ThirdPartyNotices.txt', 'CommunityToolkit-ThirdPartyNotices.txt')
}
foreach ($id in $notices.Keys) {
    $source = Join-Path (Get-PackageDir $id) $notices[$id][0]
    if (-not (Test-Path $source)) { throw "notices file missing: $source" }
    Copy-Item $source (Join-Path $licenses $notices[$id][1])
}

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
