param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("train", "predict", "help")]
    [string]$Mode,

    [Parameter(Position = 1)]
    [string]$Preset = "big",

    [string]$DatasetDir = "data/datasets/fuel-dataset",
    [string]$ModelDir = "",
    [string]$OutputDir = "",
    [string]$MissionJson = "",
    [switch]$DryRun,

    [double]$DvTotalMps,
    [double]$TravelDays,
    [double]$SolarDistanceAu,
    [double]$MinDistanceAu,
    [double]$PayloadMassKg,
    [string]$Origin = "",
    [string]$Destination = "",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$ErrorActionPreference = "Stop"

function Show-Usage {
    Write-Host ""
    Write-Host "Rocket Builder ML launcher"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\rocket_builder_ml\run.ps1 train smoke"
    Write-Host "  .\rocket_builder_ml\run.ps1 train big"
    Write-Host "  .\rocket_builder_ml\run.ps1 train big -ExtraArgs '--batch-size','16384'"
    Write-Host "  .\rocket_builder_ml\run.ps1 train outer_planet_big"
    Write-Host "  .\rocket_builder_ml\run.ps1 predict big -ModelDir artifacts/rocket-builder-ml-tank-aware-big -DvTotalMps 10166.6 -TravelDays 283.1 -SolarDistanceAu 1.337 -MinDistanceAu 1.017 -PayloadMassKg 44470.7 -Origin Earth -Destination Mars"
    Write-Host "  .\rocket_builder_ml\run.ps1 predict big -MissionJson .\mission.json"
    Write-Host ""
    Write-Host "Train presets:"
    Write-Host "  smoke  - fast pipeline check"
    Write-Host "  base   - smaller tank-aware run"
    Write-Host "  big    - recommended default training run"
    Write-Host "  outer_planet_big - aggressive long-coast configuration"
    Write-Host ""
    Write-Host "Add your own extra flags with -ExtraArgs."
    Write-Host ""
}

if ($Mode -eq "help") {
    Show-Usage
    exit 0
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$presetPath = Join-Path $scriptDir "presets.json"

if (-not (Test-Path -LiteralPath $presetPath)) {
    throw "Preset file not found: $presetPath"
}

$presets = Get-Content -LiteralPath $presetPath -Raw | ConvertFrom-Json

if (-not $presets.PSObject.Properties.Name.Contains($Mode)) {
    throw "Unknown mode '$Mode' in presets."
}

$modePresets = $presets.$Mode
if (-not $modePresets.PSObject.Properties.Name.Contains($Preset)) {
    throw "Unknown preset '$Preset' for mode '$Mode'."
}

$presetConfig = $modePresets.$Preset
$pythonArgs = New-Object System.Collections.Generic.List[string]

if ($Mode -eq "train") {
    $pythonArgs.Add("-m")
    $pythonArgs.Add("rocket_builder_ml.train")
    $pythonArgs.Add("--dataset-dir")
    $pythonArgs.Add($DatasetDir)

    $resolvedOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) { [string]$presetConfig.output_dir } else { $OutputDir }
    $pythonArgs.Add("--output-dir")
    $pythonArgs.Add($resolvedOutputDir)
}
elseif ($Mode -eq "predict") {
    $pythonArgs.Add("-m")
    $pythonArgs.Add("rocket_builder_ml.predict")

    $resolvedModelDir = if ([string]::IsNullOrWhiteSpace($ModelDir)) { [string]$presetConfig.output_dir } else { $ModelDir }
    $pythonArgs.Add("--model-dir")
    $pythonArgs.Add($resolvedModelDir)

    if (-not [string]::IsNullOrWhiteSpace($MissionJson)) {
        $pythonArgs.Add("--input-json")
        $pythonArgs.Add($MissionJson)
    }
    else {
        if (-not $PSBoundParameters.ContainsKey("DvTotalMps")) { throw "Predict without MissionJson requires -DvTotalMps" }
        if (-not $PSBoundParameters.ContainsKey("TravelDays")) { throw "Predict without MissionJson requires -TravelDays" }
        if (-not $PSBoundParameters.ContainsKey("SolarDistanceAu")) { throw "Predict without MissionJson requires -SolarDistanceAu" }
        if (-not $PSBoundParameters.ContainsKey("PayloadMassKg")) { throw "Predict without MissionJson requires -PayloadMassKg" }

        $pythonArgs.Add("--dv-total-mps")
        $pythonArgs.Add($DvTotalMps.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        $pythonArgs.Add("--travel-days")
        $pythonArgs.Add($TravelDays.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        $pythonArgs.Add("--solar-distance-au")
        $pythonArgs.Add($SolarDistanceAu.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        $pythonArgs.Add("--payload-mass-kg")
        $pythonArgs.Add($PayloadMassKg.ToString([System.Globalization.CultureInfo]::InvariantCulture))

        if ($PSBoundParameters.ContainsKey("MinDistanceAu")) {
            $pythonArgs.Add("--min-distance-au")
            $pythonArgs.Add($MinDistanceAu.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        }
        if (-not [string]::IsNullOrWhiteSpace($Origin)) {
            $pythonArgs.Add("--origin")
            $pythonArgs.Add($Origin)
        }
        if (-not [string]::IsNullOrWhiteSpace($Destination)) {
            $pythonArgs.Add("--destination")
            $pythonArgs.Add($Destination)
        }
    }
}

foreach ($arg in $presetConfig.args) {
    $pythonArgs.Add([string]$arg)
}

if ($ExtraArgs) {
    foreach ($arg in $ExtraArgs) {
        $pythonArgs.Add($arg)
    }
}

Write-Host ""
Write-Host "Mode:        $Mode"
Write-Host "Preset:      $Preset"
Write-Host "Description: $([string]$presetConfig.description)"
Write-Host "Command:"
Write-Host ("python " + ($pythonArgs -join " "))
Write-Host ""

if ($DryRun) {
    exit 0
}

Push-Location $repoRoot
try {
    & python @pythonArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
