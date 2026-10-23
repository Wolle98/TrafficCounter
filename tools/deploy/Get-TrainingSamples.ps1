<#
.SYNOPSIS
    Copies new training samples (ADR-0026) from the counting machine into the private repository.

.DESCRIPTION
    Lists the complete samples (folders with sample.json) on the counting machine via ssh and copies those that are
    missing locally, packed into one tar archive (thousands of small files one by one over scp take hours). Samples
    already copied are skipped, so the script can run any time. The samples stay on the counting machine as well;
    this is the second copy.

    The images contain the site (anonymised, but private): the target must be in the private repository.

.EXAMPLE
    .\tools\deploy\Get-TrainingSamples.ps1 -ComputerName notebook
#>
param(
    # ssh host of the counting machine (an entry in ~/.ssh/config, key login).
    [Parameter(Mandatory)]
    [string] $ComputerName,

    [string] $RemoteDirectory = 'C:/TrafficCounter/datasets/inbox',

    [string] $Target = $(if ($env:TRAFFICCOUNTER_PRIVATE) { Join-Path $env:TRAFFICCOUNTER_PRIVATE 'datasets\inbox' }),

    # Working folder on the counting machine for the list and the archive.
    [string] $RemoteTemp = 'C:/Windows/Temp'
)

$ErrorActionPreference = 'Stop'
if (-not $Target) { throw 'Pass -Target or set TRAFFICCOUNTER_PRIVATE.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\') + '\'
if (([IO.Path]::GetFullPath($Target).TrimEnd('\') + '\').StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The target is inside the public repository ($Target); samples belong in the private one."
}

# Runs PowerShell on the counting machine; sent encoded, so no quoting has to survive two shells.
function Invoke-Remote([string] $script) {
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    ssh $ComputerName "powershell -NoProfile -EncodedCommand $encoded"
}

# "day/sample" of every complete sample on the counting machine.
$remote = @(Invoke-Remote @"
Get-ChildItem '$RemoteDirectory' -Directory | ForEach-Object {
    `$day = `$_.Name
    Get-ChildItem `$_.FullName -Directory | Where-Object { Test-Path (Join-Path `$_.FullName 'sample.json') } | ForEach-Object { `$day + '/' + `$_.Name }
}
"@ | Where-Object { $_ -match '^\d{4}-\d{2}-\d{2}/\S+$' })
if ($LASTEXITCODE -ne 0) { throw "Listing the samples on $ComputerName failed ($LASTEXITCODE)." }

$missing = @($remote | Where-Object { -not (Test-Path (Join-Path $Target "$_\sample.json")) })
Write-Host "$($remote.Count) samples on $ComputerName, $($missing.Count) new"
if ($missing.Count -eq 0) { return }

$list = Join-Path ([IO.Path]::GetTempPath()) 'trafficcounter-samples.txt'
$archive = Join-Path ([IO.Path]::GetTempPath()) 'trafficcounter-samples.tar'
$remoteList = "$RemoteTemp/trafficcounter-samples.txt"
$remoteArchive = "$RemoteTemp/trafficcounter-samples.tar"
try {
    [IO.File]::WriteAllLines($list, $missing)
    & scp -q $list "${ComputerName}:$remoteList"
    if ($LASTEXITCODE -ne 0) { throw "Copying the list failed ($LASTEXITCODE)." }

    Invoke-Remote "tar -cf '$remoteArchive' -C '$RemoteDirectory' -T '$remoteList'; exit `$LASTEXITCODE"
    if ($LASTEXITCODE -ne 0) { throw "Packing the samples on $ComputerName failed ($LASTEXITCODE)." }

    & scp -q "${ComputerName}:$remoteArchive" $archive
    if ($LASTEXITCODE -ne 0) { throw "Copying the archive failed ($LASTEXITCODE)." }

    New-Item -ItemType Directory -Force $Target | Out-Null
    & tar -xf $archive -C $Target
    if ($LASTEXITCODE -ne 0) { throw "Unpacking failed ($LASTEXITCODE)." }
}
finally {
    foreach ($file in $list, $archive) { if (Test-Path $file) { [IO.File]::Delete($file) } }
    Invoke-Remote "foreach (`$f in '$remoteList', '$remoteArchive') { if (Test-Path `$f) { [IO.File]::Delete(`$f) } }" | Out-Null
}

foreach ($day in $missing | Group-Object { $_.Split('/')[0] }) {
    Write-Host "  $($day.Name): $($day.Count) copied"
}

Write-Host "$($missing.Count) samples copied to $Target"
