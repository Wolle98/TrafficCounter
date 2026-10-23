<#
.SYNOPSIS
    Builds a self-contained TrafficCounter package for another machine (e.g. a notebook), ADR-0012 / ADR-0029.

.DESCRIPTION
    Publishes the counting service for win-x64 including the .NET runtime, so the target needs no .NET installation.
    Copies the chosen ONNX models, the notebook configuration template and the install scripts into one folder that
    can be copied to the target (USB stick, network share).

.EXAMPLE
    .\tools\deploy\Publish-TrafficCounter.ps1 -Backend OpenVino -Models yolo11s-640x192.onnx, yolo11s-640x192-int8.onnx

.EXAMPLE
    .\tools\deploy\Publish-TrafficCounter.ps1 -Backend DirectML -Output D:\Deploy\TrafficCounter
#>
param(
    # DirectML: any DirectX 12 GPU incl. Intel/AMD integrated graphics, CPU fallback. OpenVino: Intel graphics/NPU/CPU.
    # Nvidia: TensorRT/CUDA on NVIDIA cards; needs the CUDA, cuDNN and TensorRT libraries on the target (ADR-0041).
    [ValidateSet('DirectML', 'OpenVino', 'Nvidia')]
    [string] $Backend = 'OpenVino',

    # Model files to include: from <repo>\models, else from the private repository's models folder (Git LFS).
    [string[]] $Models = @('yolo11s-640x192.onnx'),

    [string] $Output = (Join-Path $PSScriptRoot "..\..\dist\TrafficCounter-$Backend")
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$Output = [IO.Path]::GetFullPath($Output)

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
$app = Join-Path $Output 'app'

Write-Host "Publishing TrafficCounter ($Backend) to $app"
& dotnet publish (Join-Path $repo 'src\TrafficCounter.Service') -c Release -r win-x64 --self-contained `
    "-p:InferenceBackend=$Backend" -o $app
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$modelTarget = Join-Path $app 'models'
New-Item -ItemType Directory -Force $modelTarget | Out-Null
foreach ($model in $Models) {
    $source = Join-Path $repo "models\$model"
    if (-not (Test-Path $source) -and $env:TRAFFICCOUNTER_PRIVATE) {
        # Exported models are kept in the private repository (Git LFS).
        $source = Join-Path $env:TRAFFICCOUNTER_PRIVATE "models\$model"
    }
    if (-not (Test-Path $source)) { throw "Model $model not found in <repo>\models or the private repository - export it with tools\training\export_model.py." }
    Copy-Item $source $modelTarget
}

# Notebook defaults (reduced load); the service reads appsettings.Production.json after appsettings.json.
$settings = Get-Content (Join-Path $PSScriptRoot 'appsettings.Production.json') -Raw | ConvertFrom-Json
$settings.TrafficCounter.Detector.ModelPath = "models/$($Models[0])"
$settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $app 'appsettings.Production.json') -Encoding UTF8

Copy-Item (Join-Path $PSScriptRoot 'Install-TrafficCounter.ps1'), (Join-Path $PSScriptRoot 'Uninstall-TrafficCounter.ps1') $Output

Write-Host ""
Write-Host "Package ready: $Output"
Write-Host "Copy the folder to the target, then run there as administrator:"
Write-Host "  .\Install-TrafficCounter.ps1 -SiteConfig <path to site.json>"
Write-Host "The site configuration (private repository) and site.drift.json are NOT part of the package."
