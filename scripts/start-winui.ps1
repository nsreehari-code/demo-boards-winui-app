$ErrorActionPreference = 'Stop'

$appProcessName = 'DemoBoards.WinUI'
$appUserModelId = '3F0CE0FD-87AF-4243-9CBB-BA116FB513E1_1z32rh13vfry6!App'
$launchTimeoutSeconds = 10

Write-Host 'Starting WinUI app...'
Start-Process explorer.exe "shell:AppsFolder\$appUserModelId"

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
do {
	$process = Get-Process -Name $appProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
	if ($process) {
		Write-Host "Launched packaged app via $appUserModelId (PID: $($process.Id))"
		return
	}

	Start-Sleep -Milliseconds 250
} while ($stopwatch.Elapsed.TotalSeconds -lt $launchTimeoutSeconds)

throw "Timed out waiting for $appProcessName to appear after launching $appUserModelId."
