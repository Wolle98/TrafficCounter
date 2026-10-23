<#
.SYNOPSIS
    Starts Label Studio for labelling the training samples (ADR-0026, rules ADR-0039) at http://localhost:8089.

.DESCRIPTION
    Serves the images from the private repository's datasets folder (local file serving) and keeps Label Studio's
    own database there as well (datasets/labelstudio/data, not versioned). Install once:

        python -m venv .venv-labelstudio
        .venv-labelstudio\Scripts\pip install label-studio
#>
param(
    [int] $Port = 8089
)

$ErrorActionPreference = 'Stop'
if (-not $env:TRAFFICCOUNTER_PRIVATE) { throw 'Set TRAFFICCOUNTER_PRIVATE to the private repository.' }
$datasets = Join-Path $env:TRAFFICCOUNTER_PRIVATE 'datasets'
$exe = Join-Path $PSScriptRoot '.venv-labelstudio\Scripts\label-studio.exe'
if (-not (Test-Path $exe)) { throw "Label Studio is not installed: $exe" }

# Both spellings: Label Studio reads either, and its own messages name the one without prefix.
$env:LABEL_STUDIO_LOCAL_FILES_SERVING_ENABLED = 'true'
$env:LOCAL_FILES_SERVING_ENABLED = 'true'
$env:LABEL_STUDIO_LOCAL_FILES_DOCUMENT_ROOT = $datasets
$env:LOCAL_FILES_DOCUMENT_ROOT = $datasets
Write-Host "Local files: serving enabled, root $datasets"
$env:LABEL_STUDIO_BASE_DATA_DIR = Join-Path $datasets 'labelstudio\data'
# No usage statistics and no version check leave the machine: the project data is private.
$env:COLLECT_ANALYTICS = 'false'
$env:LATEST_VERSION_CHECK = 'false'
# Only reachable from this computer (the default would listen on all network interfaces).
& $exe start --port $Port --internal-host 127.0.0.1 --no-browser
