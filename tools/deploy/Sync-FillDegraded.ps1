<#
.SYNOPSIS
    Synchronises the databases and fills the counting camera's impaired minutes from a second camera, for a
    scheduled task.

.DESCRIPTION
    Runs, in this order:
    1. `TrafficCounter.Sync sync`, so both databases have the same rows. A fill on databases that differ leaves the
       counting camera's late rows next to the copied ones, so a vehicle would count twice.
    2. `fill-degraded` over the last hours. Only minutes the counting camera stored as not fully measured and the
       second camera measured itself; minutes without any row are never filled here. Spans in -Exclude are left out.
    3. `sync` again.
    4. With -SpeedClipsFrom: new speed clips (ADR-0053) from the counting machine, with Get-SpeedClips.ps1. A failure
       there is logged but does not fail the run, since the databases are done by then.

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

    [string] $Log = (Join-Path $env:LOCALAPPDATA 'TrafficCounter\sync-fill-degraded.log'),

    # ssh host of the counting machine to fetch new speed clips from; empty: no clips.
    [string] $SpeedClipsFrom,

    # Target folder of the clips; default as in Get-SpeedClips.ps1 (%TRAFFICCOUNTER_PRIVATE%\recordings\speed).
    [string] $SpeedClipsTarget,

    # Spans never filled, as "<from>/<to>" (ISO 8601), e.g. while the second camera counts a vehicle standing on the
    # line again and again.
    [string[]] $Exclude = @()
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
    # The look-back window minus the excluded spans, in order.
    $spans = @(, @($from, $now))
    # -File (scheduled task) passes "a/b,c/d" as one string.
    foreach ($span in @($Exclude -split ',' | Where-Object { $_ })) {
        $parts = $span -split '/'
        $skipFrom = [DateTimeOffset]::Parse($parts[0])
        $skipTo = [DateTimeOffset]::Parse($parts[1])
        $spans = @($spans | ForEach-Object {
            if ($skipFrom -gt $_[0]) { , @($_[0], $(if ($skipFrom -lt $_[1]) { $skipFrom } else { $_[1] })) }
            if ($skipTo -lt $_[1]) { , @($(if ($skipTo -gt $_[0]) { $skipTo } else { $_[0] }), $_[1]) }
        })
    }

    foreach ($span in $spans) {
        Invoke-Sync @('fill-degraded', '--from', $span[0].ToString($format), '--to', $span[1].ToString($format), '--source-db', $SourceDb)
    }
    Invoke-Sync @('sync')
}
catch {
    Write-Log "ERROR $($_.Exception.Message)"
    exit 1
}

if ($SpeedClipsFrom) {
    try {
        $clipArguments = @{ ComputerName = $SpeedClipsFrom }
        if ($SpeedClipsTarget) { $clipArguments.Target = $SpeedClipsTarget }
        $script = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Get-SpeedClips.ps1'
        Write-Log (& $script @clipArguments 6>&1 2>&1 | ForEach-Object { "clips: $_" })
    }
    catch {
        Write-Log "ERROR clips: $($_.Exception.Message)"
    }
}
