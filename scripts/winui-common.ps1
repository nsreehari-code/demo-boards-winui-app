$ErrorActionPreference = 'Stop'

function Get-WinUiDotnetPath {
	$dotnetRootCandidates = @()

	if ($env:DOTNET_ROOT) {
		$dotnetRootCandidates += $env:DOTNET_ROOT
	}

	if ($env:USERPROFILE) {
		$dotnetRootCandidates += (Join-Path $env:USERPROFILE '.dotnet')
	}

	foreach ($root in $dotnetRootCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
		$dotnetExe = Join-Path $root 'dotnet.exe'
		if (Test-Path $dotnetExe) {
			$env:DOTNET_ROOT = $root
			return $dotnetExe
		}
	}

	$command = Get-Command dotnet -ErrorAction SilentlyContinue
	if ($command -and $command.Source) {
		return $command.Source
	}

	throw 'Could not resolve a dotnet executable. Install .NET 10 or set DOTNET_ROOT.'
}

function Stop-WinUiProcesses {
	param(
		[string]$PidFile,
		[string]$AppProcessName = 'DemoBoards.WinUI'
	)

	Get-Process -Name $AppProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

	if (Test-Path $PidFile) {
		$dotnetRunPid = Get-Content -Path $PidFile -ErrorAction SilentlyContinue | Select-Object -First 1
		if ($dotnetRunPid -match '^\d+$') {
			Stop-Process -Id ([int]$dotnetRunPid) -Force -ErrorAction SilentlyContinue
		}

		Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
	}
}