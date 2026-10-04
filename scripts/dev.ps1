#!/usr/bin/env pwsh
# Rebuild and restart on every save. Usage: ./scripts/dev.ps1 fake --identity msfs2024
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet watch --project src/ClearanceLink.Host run -- @args
