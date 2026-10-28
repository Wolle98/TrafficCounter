<#
.SYNOPSIS
    Copies new speed clips (ADR-0053) from the counting machine into the private repository.

.DESCRIPTION
    Lists the complete clips on the counting machine via ssh (video with metadata, H.264 conversion finished) and
    copies those that are missing locally, with their timestamps and site geometry. Clips already copied are skipped,
    so the script can run any time. The clips stay on the counting machine as well.

    The videos contain personal data (ADR-0005): the target must be in the private repository.

.EXAMPLE
    .\tools\deploy\Get-SpeedClips.ps1 -ComputerName notebook
#>
param(
    # ssh host of the counting machine (an entry in ~/.ssh/config, key login).
    [Parameter(Mandatory)]
    [string] $ComputerName,

    [string] $RemoteDirectory = 'C:/TrafficCounter/recordings/speed',

    [string] $Target = $(if ($env:TRAFFICCOUNTER_PRIVATE) { Join-Path $env:TRAFFICCOUNTER_PRIVATE 'recordings\speed' })
)

$ErrorActionPreference = 'Stop'
if (-not $Target) { throw 'Pass -Target or set TRAFFICCOUNTER_PRIVATE.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\') + '\'
if (([IO.Path]::GetFullPath($Target).TrimEnd('\') + '\').StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The target is inside the public repository ($Target); clips belong in the private one."
}

# Runs PowerShell on the counting machine; sent encoded, so no quoting has to survive two shells.
function Invoke-Remote([string] $script) {
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    ssh $ComputerName "powershell -NoProfile -EncodedCommand $encoded"
}

# A clip is complete once its metadata exists and no Motion JPEG intermediate is left (conversion done).
$remote = @(Invoke-Remote @"
if (Test-Path '$RemoteDirectory') {
    Get-ChildItem '$RemoteDirectory' -Filter '*.mp4' | Where-Object {
        (Test-Path (`$_.FullName + '.json')) -and -not (Test-Path ([IO.Path]::ChangeExtension(`$_.FullName, '.avi')))
    } | ForEach-Object { `$_.Name }
}
"@ | Where-Object { $_ -match '^\S+\.mp4$' })
if ($LASTEXITCODE -ne 0) { throw "Listing the clips on $ComputerName failed ($LASTEXITCODE)." }

$missing = @($remote | Where-Object { -not (Test-Path (Join-Path $Target "$_.json")) })
Write-Host "$($remote.Count) speed clips on $ComputerName, $($missing.Count) new"
New-Item -ItemType Directory -Force $Target | Out-Null
foreach ($clip in $missing) {
    # The metadata last: a clip whose copy was interrupted is copied again next time.
    foreach ($suffix in '', '.frames.csv', '.site.json', '.json') {
        & scp -q "${ComputerName}:$RemoteDirectory/$clip$suffix" (Join-Path $Target "$clip$suffix")
        if ($LASTEXITCODE -ne 0 -and $suffix -ne '.site.json') { throw "Copying $clip$suffix failed ($LASTEXITCODE)." }
    }

    Write-Host "  $clip"
}
