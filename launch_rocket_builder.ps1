param(
    [ValidateSet("", "gui", "predict", "train", "list-models", "porkchop")]
    [string]$ModeOverride = "",
    [string]$ModelOverride = "",
    [switch]$PreviewOverride
)

$ErrorActionPreference = "Stop"

# ============================================
# SIMPLE SETTINGS
# Edit this block for normal day-to-day use.
# ============================================

$Mode = "gui"                    # gui | predict | train | list-models
$ModelSelection = "menu"         # auto | menu | 1 | exact model folder name | full path
$Device = "auto"                 # auto | cpu | cuda
$TopK = 5
$PreviewOnly = $false            # $true = only print the command

$UseMissionJson = $false
$MissionJsonPath = "data/results/rocket-builder-mission.json"
$OutputJsonPath = "data/results/rocket-builder-prediction.json"

$SimpleMission = @{
    DvTotalMps      = 10166.6
    TravelDays      = 283.1
    SolarDistanceAu = 1.337
    MinDistanceAu   = 1.017
    PayloadMassKg   = 44470.7
    Origin          = "Earth"
    Destination     = "Mars"
}

$TrainSettings = @{
    Preset     = "big"                               # smoke | base | big | outer_planet_big
    DatasetDir = "data/datasets/fuel-dataset-v6-big"
    OutputDir  = ""                                  # empty = use preset default
    ExtraArgs  = @()                                 # example: @("--epochs","96","--batch-size","7168")
}

# ============================================
# ADVANCED SETTINGS
# Leave null / empty values if you do not need them.
# ============================================

$RuntimeSettings = @{
    PythonExecutable = "python"
    BatchSize        = 1024
}

$DetailedMission = @{
    Enabled                 = $false
    DvEjectionMps           = $null
    DvInjectionMps          = $null
    LaunchAscentDvMps       = $null
    DepartureOrbitKm        = $null
    DepartureOrbitPeriapsisKm = $null
    DepartureOrbitApoapsisKm  = $null
    ArrivalOrbitKm          = $null
    DepartureDistanceAu     = $null
    ArrivalDistanceAu       = $null
    DepartureSolarFluxWm2   = $null
    MeanSolarFluxWm2        = $null
    PhaseAngleDeg           = $null
    TransferAngleDeg        = $null
    LongWay                 = $false
    SurfaceLaunch           = $false
    CorrectionReserveDvMps  = $null
}

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not [string]::IsNullOrWhiteSpace($ModeOverride)) {
    $Mode = $ModeOverride
}
if (-not [string]::IsNullOrWhiteSpace($ModelOverride)) {
    $ModelSelection = $ModelOverride
}
if ($PreviewOverride.IsPresent) {
    $PreviewOnly = $true
}

function Resolve-RepoPath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $PathValue))
}

function Add-Argument {
    param(
        [System.Collections.Generic.List[string]]$Arguments,
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $Arguments.Add($Name)
    $Arguments.Add($Value)
}

function Add-NumericArgument {
    param(
        [System.Collections.Generic.List[string]]$Arguments,
        [string]$Name,
        [object]$Value
    )

    if ($null -eq $Value) {
        return
    }

    $Arguments.Add($Name)
    $Arguments.Add(([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0}", $Value)))
}

function Add-SwitchArgument {
    param(
        [System.Collections.Generic.List[string]]$Arguments,
        [string]$Name,
        [bool]$Enabled
    )

    if ($Enabled) {
        $Arguments.Add($Name)
    }
}

function Get-AvailableModels {
    $artifactsDir = Join-Path $RepoRoot "artifacts"
    if (-not (Test-Path -LiteralPath $artifactsDir)) {
        return @()
    }

    $models = @()
    foreach ($directory in Get-ChildItem -LiteralPath $artifactsDir -Directory | Sort-Object Name) {
        $modelPath = Join-Path $directory.FullName "model.pt"
        $metadataPath = Join-Path $directory.FullName "metadata.json"
        if (-not (Test-Path -LiteralPath $modelPath) -or -not (Test-Path -LiteralPath $metadataPath)) {
            continue
        }

        $featureVersion = "-"
        $maxStageCount = "-"
        try {
            $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            if ($metadata.PSObject.Properties.Name -contains "feature_version") {
                $featureVersion = [string]$metadata.feature_version
            }
            if ($metadata.PSObject.Properties.Name -contains "max_stage_count") {
                $maxStageCount = [string]$metadata.max_stage_count
            }
        }
        catch {
            # Metadata details are optional for listing.
        }

        $models += [pscustomobject]@{
            Index          = 0
            Name           = $directory.Name
            RelativePath   = ("artifacts/" + $directory.Name)
            FullPath       = $directory.FullName
            LastWriteTime  = $directory.LastWriteTime
            FeatureVersion = $featureVersion
            MaxStageCount  = $maxStageCount
        }
    }

    for ($i = 0; $i -lt $models.Count; $i++) {
        $models[$i].Index = $i + 1
    }

    return $models
}

function Show-AvailableModels {
    param([object[]]$Models)

    if ($Models.Count -eq 0) {
        Write-Host "No valid model directories were found in artifacts."
        return
    }

    Write-Host ""
    Write-Host "Available model versions:"
    foreach ($model in $Models) {
        Write-Host ("  [{0}] {1}" -f $model.Index, $model.Name)
        Write-Host ("      path: {0}" -f $model.RelativePath)
        Write-Host ("      updated: {0:yyyy-MM-dd HH:mm}" -f $model.LastWriteTime)
        Write-Host ("      feature_version: {0} | max_stage_count: {1}" -f $model.FeatureVersion, $model.MaxStageCount)
    }
    Write-Host ""
}

function Resolve-DefaultModel {
    param([object[]]$Models)

    $preferredNames = @(
        "rocket-builder-ml-tank-aware-big",
        "rocket-builder-ml-tank-aware",
        "rocket-builder-ml-v3-stage-dv-big-retrained",
        "rocket-builder-ml-v3-stage-dv-big",
        "rocket-builder-ml-engine-aware"
    )

    foreach ($preferred in $preferredNames) {
        $match = $Models | Where-Object { $_.Name -eq $preferred } | Select-Object -First 1
        if ($null -ne $match) {
            return $match
        }
    }

    return $Models | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

function Resolve-SelectedModel {
    param(
        [string]$Selection,
        [object[]]$Models
    )

    if ($Models.Count -eq 0) {
        throw "No model versions were found in the artifacts folder."
    }

    if ([string]::IsNullOrWhiteSpace($Selection) -or $Selection -eq "auto") {
        return Resolve-DefaultModel -Models $Models
    }

    if ($Selection -eq "menu") {
        Show-AvailableModels -Models $Models
        $answer = Read-Host "Choose model number or exact folder name"
        return Resolve-SelectedModel -Selection $answer -Models $Models
    }

    if ($Selection -match "^\d+$") {
        $index = [int]$Selection
        $byIndex = $Models | Where-Object { $_.Index -eq $index } | Select-Object -First 1
        if ($null -ne $byIndex) {
            return $byIndex
        }
        throw "Model index '$Selection' is out of range."
    }

    $resolvedPath = Resolve-RepoPath -PathValue $Selection
    if (Test-Path -LiteralPath $resolvedPath) {
        $modelPath = Join-Path $resolvedPath "model.pt"
        $metadataPath = Join-Path $resolvedPath "metadata.json"
        if ((Test-Path -LiteralPath $modelPath) -and (Test-Path -LiteralPath $metadataPath)) {
            return [pscustomobject]@{
                Index          = 0
                Name           = Split-Path -Leaf $resolvedPath
                RelativePath   = $Selection
                FullPath       = $resolvedPath
                LastWriteTime  = (Get-Item -LiteralPath $resolvedPath).LastWriteTime
                FeatureVersion = "-"
                MaxStageCount  = "-"
            }
        }
    }

    $exactMatch = $Models | Where-Object { $_.Name -eq $Selection } | Select-Object -First 1
    if ($null -ne $exactMatch) {
        return $exactMatch
    }

    $partialMatches = $Models | Where-Object { $_.Name -like ("*" + $Selection + "*") }
    if ($partialMatches.Count -eq 1) {
        return $partialMatches[0]
    }
    if ($partialMatches.Count -gt 1) {
        throw "Model selection '$Selection' is ambiguous. Use an exact folder name or model number."
    }

    throw "Could not resolve model selection '$Selection'."
}

function Ensure-ParentDirectory {
    param([string]$FilePath)

    if ([string]::IsNullOrWhiteSpace($FilePath)) {
        return
    }

    $parent = Split-Path -Parent $FilePath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
}

function Invoke-Predict {
    $models = Get-AvailableModels
    $selectedModel = Resolve-SelectedModel -Selection $ModelSelection -Models $models

    $pythonArguments = New-Object System.Collections.Generic.List[string]
    $pythonArguments.Add("-m")
    $pythonArguments.Add("rocket_builder_ml.predict")
    $pythonArguments.Add("--model-dir")
    $pythonArguments.Add($selectedModel.FullPath)
    $pythonArguments.Add("--device")
    $pythonArguments.Add($Device)
    $pythonArguments.Add("--top-k")
    $pythonArguments.Add([string]$TopK)
    $pythonArguments.Add("--batch-size")
    $pythonArguments.Add([string]$RuntimeSettings.BatchSize)

    if ($UseMissionJson) {
        $resolvedMissionJson = Resolve-RepoPath -PathValue $MissionJsonPath
        if (-not (Test-Path -LiteralPath $resolvedMissionJson)) {
            throw "Mission JSON file not found: $resolvedMissionJson"
        }
        $pythonArguments.Add("--input-json")
        $pythonArguments.Add($resolvedMissionJson)
    }
    else {
        Add-NumericArgument -Arguments $pythonArguments -Name "--dv-total-mps" -Value $SimpleMission.DvTotalMps
        Add-NumericArgument -Arguments $pythonArguments -Name "--travel-days" -Value $SimpleMission.TravelDays
        Add-NumericArgument -Arguments $pythonArguments -Name "--solar-distance-au" -Value $SimpleMission.SolarDistanceAu
        Add-NumericArgument -Arguments $pythonArguments -Name "--min-distance-au" -Value $SimpleMission.MinDistanceAu
        Add-NumericArgument -Arguments $pythonArguments -Name "--payload-mass-kg" -Value $SimpleMission.PayloadMassKg
        Add-Argument -Arguments $pythonArguments -Name "--origin" -Value ([string]$SimpleMission.Origin)
        Add-Argument -Arguments $pythonArguments -Name "--destination" -Value ([string]$SimpleMission.Destination)

        if ($DetailedMission.Enabled) {
            Add-NumericArgument -Arguments $pythonArguments -Name "--dv-ejection-mps" -Value $DetailedMission.DvEjectionMps
            Add-NumericArgument -Arguments $pythonArguments -Name "--dv-injection-mps" -Value $DetailedMission.DvInjectionMps
            Add-NumericArgument -Arguments $pythonArguments -Name "--launch-ascent-dv-mps" -Value $DetailedMission.LaunchAscentDvMps
            Add-NumericArgument -Arguments $pythonArguments -Name "--departure-orbit-km" -Value $DetailedMission.DepartureOrbitKm
            Add-NumericArgument -Arguments $pythonArguments -Name "--departure-orbit-periapsis-km" -Value $DetailedMission.DepartureOrbitPeriapsisKm
            Add-NumericArgument -Arguments $pythonArguments -Name "--departure-orbit-apoapsis-km" -Value $DetailedMission.DepartureOrbitApoapsisKm
            Add-NumericArgument -Arguments $pythonArguments -Name "--arrival-orbit-km" -Value $DetailedMission.ArrivalOrbitKm
            Add-NumericArgument -Arguments $pythonArguments -Name "--departure-distance-au" -Value $DetailedMission.DepartureDistanceAu
            Add-NumericArgument -Arguments $pythonArguments -Name "--arrival-distance-au" -Value $DetailedMission.ArrivalDistanceAu
            Add-NumericArgument -Arguments $pythonArguments -Name "--departure-solar-flux-w-m2" -Value $DetailedMission.DepartureSolarFluxWm2
            Add-NumericArgument -Arguments $pythonArguments -Name "--mean-solar-flux-w-m2" -Value $DetailedMission.MeanSolarFluxWm2
            Add-NumericArgument -Arguments $pythonArguments -Name "--phase-angle-deg" -Value $DetailedMission.PhaseAngleDeg
            Add-NumericArgument -Arguments $pythonArguments -Name "--transfer-angle-deg" -Value $DetailedMission.TransferAngleDeg
            Add-NumericArgument -Arguments $pythonArguments -Name "--correction-reserve-dv-mps" -Value $DetailedMission.CorrectionReserveDvMps
            Add-SwitchArgument -Arguments $pythonArguments -Name "--long-way" -Enabled ([bool]$DetailedMission.LongWay)
            Add-SwitchArgument -Arguments $pythonArguments -Name "--surface-launch" -Enabled ([bool]$DetailedMission.SurfaceLaunch)
        }
    }

    $resolvedOutputJson = ""
    if (-not [string]::IsNullOrWhiteSpace($OutputJsonPath)) {
        $resolvedOutputJson = Resolve-RepoPath -PathValue $OutputJsonPath
        Ensure-ParentDirectory -FilePath $resolvedOutputJson
        $pythonArguments.Add("--output-json")
        $pythonArguments.Add($resolvedOutputJson)
    }

    Write-Host ""
    Write-Host "Rocket Builder launcher"
    Write-Host ("Mode:           predict")
    Write-Host ("Model:          {0}" -f $selectedModel.Name)
    Write-Host ("Model path:     {0}" -f $selectedModel.FullPath)
    Write-Host ("Device:         {0}" -f $Device)
    Write-Host ("Top K:          {0}" -f $TopK)
    if ($UseMissionJson) {
        Write-Host ("Mission source: {0}" -f (Resolve-RepoPath -PathValue $MissionJsonPath))
    }
    else {
        Write-Host ("Mission source: inline settings in launch_rocket_builder.ps1")
    }
    if (-not [string]::IsNullOrWhiteSpace($resolvedOutputJson)) {
        Write-Host ("Output JSON:    {0}" -f $resolvedOutputJson)
    }
    Write-Host "Command:"
    Write-Host ((@($RuntimeSettings.PythonExecutable) + $pythonArguments) -join " ")
    Write-Host ""

    if ($PreviewOnly) {
        return
    }

    Push-Location $RepoRoot
    try {
        & $RuntimeSettings.PythonExecutable @pythonArguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-Gui {
    $guiProject = Join-Path $RepoRoot "StandaloneTrajectoryCalculator.Gui\StandaloneTrajectoryCalculator.Gui.csproj"
    if (-not (Test-Path -LiteralPath $guiProject)) {
        throw "GUI project not found: $guiProject"
    }

    $dotnetArguments = @(
        "run",
        "--project",
        $guiProject
    )

    Write-Host ""
    Write-Host "Rocket Builder launcher"
    Write-Host "Mode:           gui"
    Write-Host ("Project:        {0}" -f $guiProject)
    Write-Host "Command:"
    Write-Host (("dotnet " + ($dotnetArguments -join " ")))
    Write-Host ""

    if ($PreviewOnly) {
        return
    }

    Push-Location $RepoRoot
    try {
        & dotnet @dotnetArguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-Porkchop {
    $project = Join-Path $RepoRoot "StandaloneTrajectoryCalculator\StandaloneTrajectoryCalculator.csproj"
    $inputJson = Join-Path $RepoRoot "StandaloneTrajectoryCalculator\sample-porkchop.json"

    if (-not (Test-Path -LiteralPath $project)) {
        throw "StandaloneTrajectoryCalculator project not found: $project"
    }
    if (-not (Test-Path -LiteralPath $inputJson)) {
        throw "Input JSON not found: $inputJson"
    }

    $dotnetArguments = @(
        "run",
        "--project",
        $project,
        "--",
        $inputJson
    )

    Write-Host ""
    Write-Host "Rocket Builder launcher"
    Write-Host "Mode:           porkchop"
    Write-Host ("Project:        {0}" -f $project)
    Write-Host ("Input JSON:     {0}" -f $inputJson)
    Write-Host "Command:"
    Write-Host (("dotnet " + ($dotnetArguments -join " ")))
    Write-Host ""

    if ($PreviewOnly) {
        return
    }

    Push-Location $RepoRoot
    try {
        & dotnet @dotnetArguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-Train {
    $runScript = Join-Path $RepoRoot "rocket_builder_ml\run.ps1"
    if (-not (Test-Path -LiteralPath $runScript)) {
        throw "Launcher script not found: $runScript"
    }

    $trainArguments = New-Object System.Collections.Generic.List[string]
    $trainArguments.Add("train")
    $trainArguments.Add([string]$TrainSettings.Preset)
    $trainArguments.Add("-DatasetDir")
    $trainArguments.Add((Resolve-RepoPath -PathValue $TrainSettings.DatasetDir))

    if (-not [string]::IsNullOrWhiteSpace([string]$TrainSettings.OutputDir)) {
        $trainArguments.Add("-OutputDir")
        $trainArguments.Add((Resolve-RepoPath -PathValue ([string]$TrainSettings.OutputDir)))
    }

    if ($PreviewOnly) {
        $trainArguments.Add("-DryRun")
    }

    if ($TrainSettings.ExtraArgs.Count -gt 0) {
        $trainArguments.Add("-ExtraArgs")
        foreach ($item in $TrainSettings.ExtraArgs) {
            $trainArguments.Add([string]$item)
        }
    }

    Write-Host ""
    Write-Host "Rocket Builder launcher"
    Write-Host ("Mode:           train")
    Write-Host ("Preset:         {0}" -f $TrainSettings.Preset)
    Write-Host ("Dataset:        {0}" -f (Resolve-RepoPath -PathValue $TrainSettings.DatasetDir))
    if (-not [string]::IsNullOrWhiteSpace([string]$TrainSettings.OutputDir)) {
        Write-Host ("Output dir:     {0}" -f (Resolve-RepoPath -PathValue ([string]$TrainSettings.OutputDir)))
    }
    Write-Host ("Command:")
    Write-Host ((@($runScript) + $trainArguments) -join " ")
    Write-Host ""

    & $runScript @trainArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

switch ($Mode) {
    "gui" {
        Invoke-Gui
        break
    }
    "porkchop" {
        Invoke-Porkchop
        break
    }
    "predict" {
        Invoke-Predict
        break
    }
    "train" {
        Invoke-Train
        break
    }
    "list-models" {
        Show-AvailableModels -Models (Get-AvailableModels)
        break
    }
    default {
        throw "Unsupported mode '$Mode'. Use gui, predict, train, or list-models."
    }
}
