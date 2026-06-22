$ErrorActionPreference = 'Stop'

$appProcessName = 'DemoBoards.WinUI'
$pidFile = Join-Path $PSScriptRoot '.winui-dotnet-run.pid'

Write-Host "Stopping $appProcessName if it is running..."
Get-Process -Name $appProcessName -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $pidFile) {
	$dotnetRunPid = Get-Content -Path $pidFile -ErrorAction SilentlyContinue | Select-Object -First 1
	if ($dotnetRunPid -match '^\d+$') {
		Stop-Process -Id ([int]$dotnetRunPid) -Force -ErrorAction SilentlyContinue
	}
	Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

Write-Host 'Stop complete.'
