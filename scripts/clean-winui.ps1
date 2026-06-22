$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'app/DemoBoards.WinUI'
$binDir = Join-Path $projectDir 'bin'
$objDir = Join-Path $projectDir 'obj'

Write-Host 'Cleaning WinUI app...'
dotnet clean $projectDir
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
