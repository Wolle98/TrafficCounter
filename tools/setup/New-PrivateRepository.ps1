<#
.SYNOPSIS
    Creates the private repository next to this public one (ADR-0002) and points TRAFFICCOUNTER_PRIVATE to it.

.DESCRIPTION
    The public repository contains no site data. Everything that is specific to a camera location lives in a second,
    private repository: calibration, reference videos, hand counts, models, database backups. The tools find it
    through the user environment variable TRAFFICCOUNTER_PRIVATE.

    The script:
    - creates the folder "<name of this repository>Private" next to it (or -Path),
    - creates the folder structure the tools expect (config, recordings, models, ...) and a README,
    - copies config\site.example.json to config\site.json as a starting point,
    - initialises Git with Git LFS for videos, images, models and backups (if Git LFS is installed),
    - sets the user environment variable TRAFFICCOUNTER_PRIVATE (unless -NoEnvironment),
    - optionally creates a private GitHub repository with the GitHub CLI and pushes to it (-GitHub).

    It can be run again at any time: existing files are never overwritten, only missing parts are added.

.EXAMPLE
    .\tools\setup\New-PrivateRepository.ps1

.EXAMPLE
    .\tools\setup\New-PrivateRepository.ps1 -Path E:\Data\TrafficCounterPrivate -GitHub
#>
param(
    # Target folder; default: next to the public repository, with "Private" appended to its name.
    [string] $Path,

    # Do not set TRAFFICCOUNTER_PRIVATE (e.g. on a test machine).
    [switch] $NoEnvironment,

    # Also create a private repository on GitHub (needs the GitHub CLI "gh", logged in) and push to it.
    [switch] $GitHub
)

$ErrorActionPreference = 'Stop'
$publicRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$publicName = Split-Path $publicRoot -Leaf
$privateName = "${publicName}Private"
if (-not $Path) { $Path = Join-Path (Split-Path $publicRoot -Parent) $privateName }
$Path = [IO.Path]::GetFullPath($Path)

if ($Path.StartsWith($publicRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The private repository must not be inside the public one ($publicRoot)."
}

function Write-IfMissing([string] $relative, [string] $content) {
    $file = Join-Path $Path $relative
    if (Test-Path $file) {
        Write-Host "  kept     $relative"
        return
    }

    New-Item -ItemType Directory -Force (Split-Path $file -Parent) | Out-Null
    [IO.File]::WriteAllText($file, $content.Replace("`r`n", "`n").Replace("`n", "`r`n"), (New-Object Text.UTF8Encoding($false)))
    Write-Host "  created  $relative"
}

Write-Host "Private repository: $Path"
New-Item -ItemType Directory -Force $Path | Out-Null

# 1. Folders the tools expect (see README below) and the site configuration from the public example.
$folders = @('config', 'config\reference', 'recordings', 'models', 'datasets', 'db', 'reports', 'docs')
foreach ($folder in $folders) {
    New-Item -ItemType Directory -Force (Join-Path $Path $folder) | Out-Null
}

$siteConfig = Join-Path $Path 'config\site.json'
if (-not (Test-Path $siteConfig)) {
    Copy-Item (Join-Path $publicRoot 'config\site.example.json') $siteConfig
    Write-Host "  created  config\site.json (from config\site.example.json - calibrate it with TrafficCounter.Calibrator)"
}
else {
    Write-Host "  kept     config\site.json"
}

# 2. Empty folders get a .gitkeep, so Git keeps them.
foreach ($folder in $folders) {
    if (-not (Get-ChildItem (Join-Path $Path $folder) -Force | Where-Object { $_.Name -ne '.gitkeep' })) {
        Write-IfMissing "$folder\.gitkeep" ''
    }
}

# 3. Git settings: large binaries in Git LFS, raw reference videos not versioned at all (too large).
Write-IfMissing '.gitattributes' @'
# Large binary files are stored in Git LFS (ADR-0002)
*.mp4 filter=lfs diff=lfs merge=lfs -text
*.mkv filter=lfs diff=lfs merge=lfs -text
*.avi filter=lfs diff=lfs merge=lfs -text
*.jpg filter=lfs diff=lfs merge=lfs -text
*.jpeg filter=lfs diff=lfs merge=lfs -text
*.png filter=lfs diff=lfs merge=lfs -text
*.onnx filter=lfs diff=lfs merge=lfs -text
*.pt filter=lfs diff=lfs merge=lfs -text
*.bak filter=lfs diff=lfs merge=lfs -text
*.zip filter=lfs diff=lfs merge=lfs -text
'@

Write-IfMissing '.gitignore' @'
# Raw reference videos are large - keep them on the NAS or a local disk.
# Metadata (*.json), timestamps (*.frames.csv), calibration snapshots (*.site.json)
# and hand counts (*.gt.csv) are versioned.
recordings/*.mp4
recordings/*.avi

# Temporary files of the tools
*.tmp
*.tmp.mp4
*.log
.vs/
.vscode/
'@

Write-IfMissing 'README.md' @"
# $privateName

Private data of the traffic counter - **never publish**.

Code and documentation are in the public repository ``$publicName`` (see ``docs/adr/0002-repository-split.md`` there).
The tools find this repository via the user variable ``TRAFFICCOUNTER_PRIVATE``.

| Folder | Content | Used by |
|--------|---------|---------|
| ``config/site.json`` | Site configuration: camera, counting line, evaluation band, kerbs, ignore zones, direction names, speed scale, weather station. **No passwords**; they belong in user secrets or environment variables. | service, Calibrator, Regression, Sync, Annotator import |
| ``config/site.drift.json`` | Reference of the camera position check; the Calibrator puts it next to ``site.json``. | service |
| ``config/reference/`` | Photos and notes on the site (calibration, measurements). | - |
| ``recordings/`` | Reference videos with ``<name>.mp4.frames.csv`` (timestamps), ``.json`` (tags), ``.site.json`` (calibration at recording time), ``.gt.csv`` (hand count, with the speedometer readings of test drives) and, from before ADR-0043, ``.speed.csv`` (speed test drives only). The videos themselves are not versioned (``.gitignore``). | CameraProbe record, Annotator, Regression |
| ``models/`` | Exported and fine-tuned models (``.onnx``, ``.pt``, Git LFS). | publish script, Regression |
| ``datasets/`` | Training images and labels (stage 3), anonymised only (ADR-0005); ``datasets/inbox`` receives collected samples (ADR-0026). | training |
| ``db/`` | Backups of the real database (Git LFS). | - |
| ``reports/`` | Generated analyses. | - |
| ``docs/`` | Notes on the site and the state of work. | - |

Large binary files go through **Git LFS** (``.gitattributes``); mind the storage quota of the host.

## Coupling to the public repository

Set by ``tools/setup/New-PrivateRepository.ps1`` in the public repository:

``````powershell
[Environment]::SetEnvironmentVariable("TRAFFICCOUNTER_PRIVATE", "$Path", "User")
``````

If the site configuration is somewhere else, ``TRAFFICCOUNTER_CONFIG`` points to the JSON file.
"@

Write-IfMissing 'CLAUDE.md' @"
# $privateName - notes for Claude

- Private data for the public project ``$publicRoot`` (read its ``CLAUDE.md``, roadmap and ADRs first). Never copy anything from here into the public repo.
- Large binaries (images, videos, models, DB backups) go through Git LFS (``.gitattributes``). Secrets never go into this repo either.
- Issues (ADR-0022 in the public repo): problems involving location, camera setup, recordings, datasets or real counting data are tracked here. Public issues never reference this repo.
- Docs and commit messages in English.
"@

# 4. Git repository with LFS.
$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) {
    Write-Warning 'Git not found - the folder is ready, but not a Git repository yet.'
}
else {
    Push-Location $Path
    # Native commands write progress to stderr; with "Stop" Windows PowerShell 5.1 would treat that as an error.
    $ErrorActionPreference = 'Continue'
    try {
        if (-not (Test-Path (Join-Path $Path '.git'))) {
            & git init -b main | Out-Null
            Write-Host '  created  Git repository (branch main)'
        }

        & git lfs version 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & git lfs install --local | Out-Null
            Write-Host '  Git LFS active'
        }
        else {
            Write-Warning 'Git LFS not installed (https://git-lfs.com) - install it before adding videos or models.'
        }

        & git rev-parse --verify HEAD 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            & git add -A
            & git commit -q -m "Private data repository for $publicName"
            Write-Host '  created  first commit'
        }

        if ($GitHub) {
            $gh = Get-Command gh -ErrorAction SilentlyContinue
            if (-not $gh -and (Test-Path 'C:\Program Files\GitHub CLI\gh.exe')) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
            if (-not $gh) { throw 'GitHub CLI (gh) not found.' }
            if (& git remote) {
                Write-Host '  kept     existing remote'
            }
            else {
                & $gh repo create $privateName --private --source . --push
                if ($LASTEXITCODE -ne 0) { throw "gh repo create failed ($LASTEXITCODE)" }
                Write-Host "  created  private GitHub repository $privateName"
            }
        }
    }
    finally {
        Pop-Location
        $ErrorActionPreference = 'Stop'
    }
}

# 5. Environment variable for the tools (user scope; new terminals and IDEs see it after a restart).
if ($NoEnvironment) {
    Write-Host 'TRAFFICCOUNTER_PRIVATE not changed (-NoEnvironment).'
}
else {
    $current = [Environment]::GetEnvironmentVariable('TRAFFICCOUNTER_PRIVATE', 'User')
    if ($current -and $current -ne $Path) {
        Write-Warning "TRAFFICCOUNTER_PRIVATE pointed to $current; it now points to $Path."
    }

    [Environment]::SetEnvironmentVariable('TRAFFICCOUNTER_PRIVATE', $Path, 'User')
    $env:TRAFFICCOUNTER_PRIVATE = $Path
    Write-Host "TRAFFICCOUNTER_PRIVATE = $Path (user variable; restart terminals and IDEs to pick it up)"
}

Write-Host ''
Write-Host 'Next steps: calibrate the camera (TrafficCounter.Calibrator --device <n>), then record reference videos'
Write-Host '(docs\reference-videos.md). For the Windows service set TRAFFICCOUNTER_CONFIG as a system variable (Install-TrafficCounter.ps1).'
