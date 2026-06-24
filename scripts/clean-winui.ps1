$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'winui-common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'app/DemoBoards.WinUI'
$projectFile = Join-Path $projectDir 'DemoBoards.WinUI.csproj'
$binDir = Join-Path $projectDir 'bin'
$objDir = Join-Path $projectDir 'obj'
$pidFile = Join-Path $PSScriptRoot '.winui-dotnet-run.pid'
$dotnet = Get-WinUiDotnetPath

Write-Host 'Cleaning WinUI app...'
Stop-WinUiProcesses -PidFile $pidFile

& $dotnet clean $projectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean failed with exit code $LASTEXITCODE"
}

if (Test-Path $binDir) {
    Remove-Item -Recurse -Force $binDir
}
if (Test-Path $objDir) {
    Remove-Item -Recurse -Force $objDir
}

Write-Host 'Clean complete.'
