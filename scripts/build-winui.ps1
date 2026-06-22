$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'app/DemoBoards.WinUI'
$xamlGuardScript = Join-Path $PSScriptRoot 'check-xaml-drift.mjs'

Write-Host 'Building WinUI app...'
node $xamlGuardScript
if ($LASTEXITCODE -ne 0) {
    throw "XAML drift guard failed with exit code $LASTEXITCODE"
}

dotnet build $projectDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}
