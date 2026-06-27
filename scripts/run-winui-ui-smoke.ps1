$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repoRoot 'app/tools/WinUiDesktopAutomation/WinUiDesktopAutomation.csproj'

if (-not (Test-Path $projectFile)) {
	throw "Could not find automation project at $projectFile"
}

dotnet run --project $projectFile -- $args