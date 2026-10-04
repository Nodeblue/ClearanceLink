#!/usr/bin/env pwsh
# Run ClearanceLink from source. Usage: ./scripts/run.ps1 fake --identity msfs2024
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project src/ClearanceLink.Host -- @args
exit $LASTEXITCODE
