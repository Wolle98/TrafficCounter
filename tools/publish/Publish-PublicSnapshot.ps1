<#
.SYNOPSIS
    Publishes the state of a version tag of TrafficCounterWork as one commit in the public repository (ADR-0045).

.DESCRIPTION
    The working repository (private, with its full history) is never published itself. For every version (ADR-0038)
    this script takes the tracked files of the tag (git archive), leaves out what only belongs to the working
    repository, and checks every text line against the patterns of the private repository
    (config/publish-denylist.txt) plus the direction names, coordinates and weather station of the private site
    configuration. A single hit stops the script before anything is written.

    Otherwise it replaces the content of a local clone of the public repository with the snapshot and commits it as
    "TrafficCounter <version>" with the CHANGELOG section of that version as message, tagged v<version>. Without
    -Push nothing leaves the machine; check the commit in the clone first.

.EXAMPLE
    .\tools\publish\Publish-PublicSnapshot.ps1 -PublicRepository D:\Code\TrafficCounterPublic
    .\tools\publish\Publish-PublicSnapshot.ps1 -PublicRepository D:\Code\TrafficCounterPublic -Push
#>
param(
    # Local clone of the public repository (Wolle98/TrafficCounter).
    [Parameter(Mandatory)]
    [string] $PublicRepository,

    # Version tag to publish; default the highest v* tag.
    [string] $Tag,

    [string] $PrivateRoot = $env:TRAFFICCOUNTER_PRIVATE,

    # Pushes branch and tag of the public clone after the commit.
    [switch] $Push,

    # Confirms that commits made directly in the public repository are already in the working repository.
    [switch] $PublicEditsCarriedOver
)

$ErrorActionPreference = 'Stop'
$work = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $PrivateRoot -or -not (Test-Path $PrivateRoot)) { throw 'Pass -PrivateRoot or set TRAFFICCOUNTER_PRIVATE.' }
if (-not (Test-Path (Join-Path $PublicRepository '.git'))) { throw "$PublicRepository is not a git clone." }
$PublicRepository = (Resolve-Path $PublicRepository).Path
if (($PublicRepository.TrimEnd('\') + '\').StartsWith($work.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The public clone must not lie inside the working repository.' }

# Only in the working repository: Dependabot PRs would otherwise have to be carried back by hand.
$excluded = @('.github/dependabot.yml')

if (-not $Tag) { $Tag = (git -C $work tag --list 'v*' --sort=-v:refname | Select-Object -First 1) }
if (-not $Tag) { throw 'No version tag found.' }
git -C $work rev-parse --verify --quiet "refs/tags/$Tag" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Tag $Tag does not exist." }
$version = $Tag.TrimStart('v')

# A snapshot committed in an earlier run without -Push: only push it.
git -C $PublicRepository rev-parse --verify --quiet "refs/tags/$Tag" | Out-Null
if ($LASTEXITCODE -eq 0) {
    if (-not $Push) { throw "The public repository already has $Tag; pass -Push to push it." }
    git -C $PublicRepository push -q origin HEAD
    git -C $PublicRepository push -q origin $Tag
    Write-Host "Pushed $Tag."
    return
}

# --- Patterns ---------------------------------------------------------------------------------------------------
$patterns = [System.Collections.Generic.List[object]]::new()
$denylist = Join-Path $PrivateRoot 'config\publish-denylist.txt'
if (-not (Test-Path $denylist)) { throw "$denylist is missing." }
foreach ($line in Get-Content $denylist -Encoding UTF8) {
    if ($line.Trim() -and -not $line.TrimStart().StartsWith('#')) {
        $patterns.Add([pscustomobject]@{ Label = $line.Trim(); Regex = [regex]::new($line.Trim(), 'IgnoreCase') })
    }
}

# Values of the private site configuration; only their names are printed.
foreach ($site in Get-ChildItem (Join-Path $PrivateRoot 'config') -Filter 'site*.json' | Where-Object Name -notlike '*.drift.json') {
    $json = (Get-Content $site.FullName -Raw -Encoding UTF8 | ConvertFrom-Json).TrafficCounter
    $values = @{
        'Site.DirectionAName' = $json.Site.DirectionAName
        'Site.DirectionBName' = $json.Site.DirectionBName
        'Site.Latitude' = $(if ($null -ne $json.Site.Latitude) { [Math]::Truncate([double]$json.Site.Latitude * 1000) / 1000 })
        'Site.Longitude' = $(if ($null -ne $json.Site.Longitude) { [Math]::Truncate([double]$json.Site.Longitude * 1000) / 1000 })
        'Weather.StationId' = $json.Weather.StationId
    }
    foreach ($key in $values.Keys) {
        $value = $values[$key]
        if ($null -eq $value -or "$value" -eq '') { continue }
        $text = if ($value -is [double]) { $value.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture) } else { "$value" }
        $regex = if ($value -is [double]) { [regex]::Escape($text) } else { '\b' + [regex]::Escape($text) + '\b' }
        $patterns.Add([pscustomobject]@{ Label = "$($site.Name): $key"; Regex = [regex]::new($regex, 'IgnoreCase') })
    }
}

# --- Snapshot ---------------------------------------------------------------------------------------------------
$snapshot = Join-Path ([IO.Path]::GetTempPath()) "trafficcounter-snapshot-$version"
if (Test-Path $snapshot) { Remove-Item $snapshot -Recurse -Force }
New-Item -ItemType Directory $snapshot | Out-Null
$archive = "$snapshot.zip"
git -C $work archive --format=zip -o $archive $Tag
if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
Expand-Archive $archive $snapshot
Remove-Item $archive
foreach ($path in $excluded) {
    $full = Join-Path $snapshot $path
    if (Test-Path $full) { Remove-Item $full -Recurse -Force }
}

$hits = 0
foreach ($file in Get-ChildItem $snapshot -Recurse -File) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ([Array]::IndexOf($bytes, [byte]0, 0, [Math]::Min($bytes.Length, 8000)) -ge 0) { continue } # binary
    $relative = $file.FullName.Substring($snapshot.Length + 1)
    $number = 0
    foreach ($line in [Text.Encoding]::UTF8.GetString($bytes) -split "`n") {
        $number++
        foreach ($pattern in $patterns) {
            if ($pattern.Regex.IsMatch($line)) {
                Write-Host "${relative}:${number}: $($pattern.Label)" -ForegroundColor Red
                $hits++
            }
        }
    }
}

if ($hits -gt 0) {
    Remove-Item $snapshot -Recurse -Force
    throw "$hits forbidden pattern(s) in $Tag; nothing was published. Fix them in the working repository and tag again."
}

Write-Host "$Tag checked against $($patterns.Count) patterns: clean."

# --- Commit in the public clone ---------------------------------------------------------------------------------
if (git -C $PublicRepository status --porcelain) { throw "$PublicRepository has uncommitted changes." }

# Commits made directly in the public repository (e.g. an edit on GitHub) would be overwritten by the snapshot:
# carry them over into the working repository first, then confirm with -PublicEditsCarriedOver.
if (git -C $PublicRepository remote) {
    git -C $PublicRepository pull -q --ff-only
    if ($LASTEXITCODE -ne 0) { throw "Could not fast-forward $PublicRepository." }
}
$lastSnapshot = git -C $PublicRepository tag --list 'v*' --sort=-v:refname | Select-Object -First 1
if ($lastSnapshot) {
    $edits = git -C $PublicRepository log --format='%h %s' "$lastSnapshot..HEAD"
    if ($edits -and -not $PublicEditsCarriedOver) {
        $edits | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "The public repository has commits after $lastSnapshot (above). Carry them over into the working repository, tag again and pass -PublicEditsCarriedOver."
    }
}

Get-ChildItem $PublicRepository -Force | Where-Object Name -ne '.git' | Remove-Item -Recurse -Force
Copy-Item (Join-Path $snapshot '*') $PublicRepository -Recurse -Force
Remove-Item $snapshot -Recurse -Force

# Message: the CHANGELOG section of the version.
$changelog = Get-Content (Join-Path $PublicRepository 'CHANGELOG.md') -Encoding UTF8
$start = [Array]::FindIndex($changelog, [Predicate[string]] { param($l) $l -match "^## \[$([regex]::Escape($version))\]" })
$section = if ($start -ge 0) {
    $end = [Array]::FindIndex($changelog, $start + 1, [Predicate[string]] { param($l) $l -match '^## \[' })
    if ($end -lt 0) { $end = $changelog.Length }
    ($changelog[($start + 1)..($end - 1)] -join "`n").Trim()
}
$message = Join-Path ([IO.Path]::GetTempPath()) "trafficcounter-snapshot-$version.txt"
[IO.File]::WriteAllText($message, "TrafficCounter $version`n`n$section`n", [Text.UTF8Encoding]::new($false))

git -C $PublicRepository add -A
git -C $PublicRepository commit -q -F $message
if ($LASTEXITCODE -ne 0) { throw 'Commit failed (no changes?).' }
Remove-Item $message
git -C $PublicRepository tag $Tag
Write-Host "Committed and tagged $Tag in $PublicRepository."

if ($Push) {
    git -C $PublicRepository push -q origin HEAD
    git -C $PublicRepository push -q origin $Tag
    Write-Host 'Pushed.'
} else {
    Write-Host "Check it (git -C $PublicRepository show --stat), then push with -Push or by hand."
}
