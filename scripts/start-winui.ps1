$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'winui-common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'app/DemoBoards.WinUI'
$projectFile = Join-Path $projectDir 'DemoBoards.WinUI.csproj'
$appProcessName = 'DemoBoards.WinUI'
$pidFile = Join-Path $PSScriptRoot '.winui-dotnet-run.pid'
$launchTimeoutSeconds = 30
$dotnet = Get-WinUiDotnetPath

if (-not (Test-Path $projectFile)) {
	throw "Could not find project file at $projectFile. Run npm run winui:build first."
}

Write-Host 'Starting WinUI app via dotnet run (registers package identity and launches by AUMID)...'
Stop-WinUiProcesses -PidFile $pidFile -AppProcessName $appProcessName

# A packaged WinUI 3 app must be launched WITH its MSIX package identity, otherwise
# WinUI type activation fails (REGDB_E_CLASSNOTREG) and the window never appears.
# Microsoft.Windows.SDK.BuildTools.WinApp hooks `dotnet run` to register the debug
# identity and launch the app by AUMID. dotnet run stays attached to the app, so we
# launch it detached and record its PID for stop-winui.ps1 to clean up.
$dotnetRun = Start-Process -FilePath $dotnet `
	-ArgumentList @('run', '--project', $projectFile) `
	-WorkingDirectory $projectDir `
	-PassThru `
	-WindowStyle Hidden

Set-Content -Path $pidFile -Value $dotnetRun.Id

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
do {
	$app = Get-Process -Name $appProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
	if ($app) {
		Write-Host "Launched $appProcessName (PID: $($app.Id)) via dotnet run (host PID: $($dotnetRun.Id))."
		return
	}

	if ($dotnetRun.HasExited) {
		throw "dotnet run (host PID: $($dotnetRun.Id)) exited before $appProcessName appeared. Run npm run winui:build and check %TEMP%\DemoBoards.WinUI.startup.log."
	}

	Start-Sleep -Milliseconds 300
} while ($stopwatch.Elapsed.TotalSeconds -lt $launchTimeoutSeconds)

throw "Timed out waiting for $appProcessName to appear after launching via dotnet run."
