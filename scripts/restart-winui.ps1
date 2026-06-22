$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'stop-winui.ps1')
& (Join-Path $PSScriptRoot 'build-winui.ps1')
& (Join-Path $PSScriptRoot 'start-winui.ps1')
