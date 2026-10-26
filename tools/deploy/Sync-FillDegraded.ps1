<#
.SYNOPSIS
    Synchronises the databases and fills the counting camera's impaired minutes from a second camera, for a
    scheduled task.

.DESCRIPTION
    Runs, in this order:
    1. `TrafficCounter.Sync sync`, so both databases have the same rows. A fill on databases that differ leaves the
       counting camera's late rows next to the copied ones, so a vehicle would count twice.
    2. `fill-degraded` over the last hours. Only minutes the counting camera stored as not fully measured and the
       second camera measured itself; minutes without any row are never filled here.
    3. `sync` again.

    Every run appends its output to a log file. The second camera's database is read only.

.EXAMPLE
    .\tools\deploy\Sync-FillDegraded.ps1 -SourceDb "Server=localhost;Database=TrafficCounter_Window;Trusted_Connection=True;TrustServerCertificate=True" -NotBefore 2026-10-24T00:15:00+02:00
#>
param(
    # SQL Server connection of the second camera's own database.
    [Parameter(Mandatory)]
    [string] $SourceDb,

    # Look back this many hours.
    [int] $Hours = 48,

    # Never fill before this moment, e.g. when the second camera only counts with the same model since then.
    [DateTimeOffset] $NotBefore = [DateTimeOffset]::MinValue,

    # Default: the Debug build of TrafficCounter.Sync in this repository.
    [string] $SyncDll,

    [string] $Log = (Join-Path $env:LOCALAPPDATA 'TrafficCounter\sync-fill-degraded.log')
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is empty in parameter defaults when Windows PowerShell 5.1 runs a script with -File (scheduled task).
if (-not $SyncDll) {
    $SyncDll = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\src\TrafficCounter.Sync\bin\Debug\net10.0\TrafficCounter.Sync.dll'
}

New-Item -ItemType Directory -Force (Split-Path $Log) | Out-Null
$now = [DateTimeOffset]::Now
$from = $now.AddHours(-$Hours)
if ($from -lt $NotBefore) { $from = $NotBefore }
$format = 'yyyy-MM-ddTHH:mm:sszzz'

function Write-Log([string[]] $lines) {
    $stamp = [DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss')
    $lines | Where-Object { $_ } | ForEach-Object { "$stamp $_" } | Add-Content $Log -Encoding UTF8
}

function Invoke-Sync([string[]] $arguments) {
    $output = & dotnet $SyncDll @arguments 2>&1 | ForEach-Object { "$_" }
    Write-Log $output
    if ($LASTEXITCODE -ne 0) { throw "TrafficCounter.Sync $($arguments[0]) failed ($LASTEXITCODE)." }
}

try {
    Write-Log "--- fill $($from.ToString($format)) .. $($now.ToString($format))"
    Invoke-Sync @('sync')
    Invoke-Sync @('fill-degraded', '--from', $from.ToString($format), '--to', $now.ToString($format), '--source-db', $SourceDb)
    Invoke-Sync @('sync')
}
catch {
    Write-Log "ERROR $($_.Exception.Message)"
    exit 1
}
