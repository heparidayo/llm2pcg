[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $projectRoot 'Web'
$coreProject = Join-Path $projectRoot 'Standalone\Llm2Pcg.CoreHost\Llm2Pcg.CoreHost.csproj'

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$npmCommand = Get-Command npm -ErrorAction Stop

Push-Location $webRoot
try {
    & $npmCommand.Source ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    & $npmCommand.Source test
    if ($LASTEXITCODE -ne 0) { throw "Web tests failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }

& $npmCommand.Source --prefix (Join-Path $projectRoot 'MCP') test
if ($LASTEXITCODE -ne 0) { throw "MCP tests failed with exit code $LASTEXITCODE." }

& $dotnetCommand.Source build $coreProject
if ($LASTEXITCODE -ne 0) { throw "CoreHost build failed with exit code $LASTEXITCODE." }

& $dotnetCommand.Source run --project $coreProject --no-build -- --self-test
if ($LASTEXITCODE -ne 0) { throw "CoreHost self-test failed with exit code $LASTEXITCODE." }

& node (Join-Path $PSScriptRoot 'Test-SpatialV4.mjs')
if ($LASTEXITCODE -ne 0) { throw "v4 gallery replay checks failed with exit code $LASTEXITCODE." }

& (Join-Path $PSScriptRoot 'Start-StandaloneWeb.ps1') -WebPort 43100 -CorePort 43101 -ValidateOnly
if ($LASTEXITCODE -ne 0) { throw "End-to-end smoke test failed with exit code $LASTEXITCODE." }

Write-Host 'Clone verification passed: Web/MCP tests, CoreHost build/self-test, seven legacy worlds and eight v4 gallery replays.' -ForegroundColor Green
