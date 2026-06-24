$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'winui-common.ps1')

$appProcessName = 'DemoBoards.WinUI'
$pidFile = Join-Path $PSScriptRoot '.winui-dotnet-run.pid'

Write-Host "Stopping $appProcessName if it is running..."
Stop-WinUiProcesses -PidFile $pidFile -AppProcessName $appProcessName

Write-Host 'Stop complete.'
