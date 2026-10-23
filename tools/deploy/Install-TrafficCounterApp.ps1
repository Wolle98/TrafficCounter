<#
.SYNOPSIS
    Installs the analysis app (TrafficCounter.App, ADR-0014) as a Windows service. Run as administrator.

.DESCRIPTION
    - publishes nothing itself: run Publish-TrafficCounterApp.ps1 first and copy its folder to the target,
    - copies the app to the install directory (default C:\Program Files\TrafficCounterApp),
    - uses the same database settings as the counting service (system variables ConnectionStrings__TrafficCounter,
      TrafficCounter__Storage__Provider and TRAFFICCOUNTER_PG_PASSWORD, set by Install-TrafficCounter.ps1),
    - creates the service "TrafficCounterApp" (automatic, delayed start, restart after a crash),
    - optionally opens the port in the Windows firewall for the private network (home network) only.

    The app has no login. Never make it reachable from the internet (no port forwarding on the router).

.EXAMPLE
    .\Install-TrafficCounterApp.ps1 -Port 5080 -OpenFirewall
#>
#Requires -RunAsAdministrator
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'TrafficCounterApp'),

    [int] $Port = 5080,

    # Firewall rule for the private profile only (home network), not for public networks.
    [switch] $OpenFirewall
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TrafficCounterApp'
$package = Join-Path $PSScriptRoot 'app-web'

if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script in Windows PowerShell 5.1 (powershell.exe), not PowerShell 7.' }
if (-not (Test-Path (Join-Path $package 'TrafficCounter.App.exe'))) { throw "No package in $package - run Publish-TrafficCounterApp.ps1 first." }
if (-not [Environment]::GetEnvironmentVariable('ConnectionStrings__TrafficCounter', 'Machine')) {
    throw 'No database configured (ConnectionStrings__TrafficCounter). Install the counting service first (Install-TrafficCounter.ps1).'
}

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Process | Where-Object { $_.Path -like "$InstallDir\*" }) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
}

Write-Host "Copying to $InstallDir"
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item (Join-Path $package '*') $InstallDir -Recurse -Force

# Listen on all addresses of this machine; the firewall decides who gets through.
$settingsPath = Join-Path $InstallDir 'appsettings.Production.json'
$provider = [Environment]::GetEnvironmentVariable('TrafficCounter__Storage__Provider', 'Machine')
if (-not $provider) { $provider = 'SqlServer' }
@{ Urls = "http://0.0.0.0:$Port"; Storage = @{ Provider = $provider } } | ConvertTo-Json | Set-Content $settingsPath -Encoding UTF8

$exe = Join-Path $InstallDir 'TrafficCounter.App.exe'
if (-not (Get-Service $serviceName -ErrorAction SilentlyContinue)) {
    & sc.exe create $serviceName binPath= "`"$exe`"" start= delayed-auto DisplayName= 'TrafficCounter Auswertung' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed ($LASTEXITCODE)" }
}

& sc.exe description $serviceName 'Analysis app of the traffic counter (browser, home network only).' | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

# The connection string comes from the system variable ConnectionStrings__TrafficCounter (as for the counting
# service), the PostgreSQL password from TRAFFICCOUNTER_PG_PASSWORD.

if ($OpenFirewall) {
    $rule = 'TrafficCounter Auswertung'
    Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $Port -Profile Private -Action Allow | Out-Null
    Write-Host "Firewall: TCP $Port open for the private (home) network"
}

Start-Service $serviceName
Start-Sleep -Seconds 5
Get-Service $serviceName | Format-Table Name, Status, StartType -AutoSize
Write-Host "Open http://$($env:COMPUTERNAME):$Port in a browser in the home network."
