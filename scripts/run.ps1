#!/usr/bin/env pwsh
# Run XPConnect from source. Usage: ./scripts/run.ps1 fake --identity msfs2024
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project src/XPConnect.Host -- @args
exit $LASTEXITCODE
