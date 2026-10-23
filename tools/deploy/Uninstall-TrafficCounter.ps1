<#
.SYNOPSIS
    Removes the TrafficCounter Windows service. Run as administrator. The database, logs and the site configuration
    are kept.
#>
#Requires -RunAsAdministrator
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'TrafficCounter'),

    # Also delete the program files.
    [switch] $RemoveFiles
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TrafficCounter'

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Service removed"
}

[Environment]::SetEnvironmentVariable('TRAFFICCOUNTER_CONFIG', $null, 'Machine')
[Environment]::SetEnvironmentVariable('ConnectionStrings__TrafficCounter', $null, 'Machine')
[Environment]::SetEnvironmentVariable('TrafficCounter__Detector__ModelPath', $null, 'Machine')
[Environment]::SetEnvironmentVariable('TrafficCounter__Storage__Provider', $null, 'Machine')
[Environment]::SetEnvironmentVariable('TRAFFICCOUNTER_PG_PASSWORD', $null, 'Machine')

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
}

Write-Host "Database, logs (%ProgramData%\TrafficCounter) and the site configuration are kept."
