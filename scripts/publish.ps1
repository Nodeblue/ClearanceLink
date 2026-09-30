#!/usr/bin/env pwsh
# Single-file Windows exe in ./publish. Usage: ./scripts/publish.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet publish src/XPConnect.Host -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
exit $LASTEXITCODE
