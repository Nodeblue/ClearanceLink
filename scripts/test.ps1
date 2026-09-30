#!/usr/bin/env pwsh
# Build and run all tests. Usage: ./scripts/test.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet test --nologo
exit $LASTEXITCODE
