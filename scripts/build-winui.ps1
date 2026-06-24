$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'winui-common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'app/DemoBoards.WinUI'
$projectFile = Join-Path $projectDir 'DemoBoards.WinUI.csproj'
$xamlGuardScript = Join-Path $PSScriptRoot 'check-xaml-drift.mjs'
$pidFile = Join-Path $PSScriptRoot '.winui-dotnet-run.pid'
$dotnet = Get-WinUiDotnetPath

Write-Host 'Building WinUI app...'
Stop-WinUiProcesses -PidFile $pidFile

node $xamlGuardScript
if ($LASTEXITCODE -ne 0) {
    throw "XAML drift guard failed with exit code $LASTEXITCODE"
}

& $dotnet build $projectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}
