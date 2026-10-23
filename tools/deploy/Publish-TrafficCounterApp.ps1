<#
.SYNOPSIS
    Builds a self-contained package of the analysis app (TrafficCounter.App, ADR-0014) for the counting machine.

.EXAMPLE
    .\tools\deploy\Publish-TrafficCounterApp.ps1
    # then copy dist\TrafficCounterApp to the notebook and run Install-TrafficCounterApp.ps1 there as administrator
#>
param(
    [string] $Output = (Join-Path $PSScriptRoot '..\..\dist\TrafficCounterApp')
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

& dotnet publish (Join-Path $repo 'src\TrafficCounter.App') -c Release -r win-x64 --self-contained -o (Join-Path $Output 'app-web')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $PSScriptRoot 'Install-TrafficCounterApp.ps1') $Output
Write-Host "Package ready: $Output"
