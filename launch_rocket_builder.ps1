param(
    [string]$Mode = "help",
    [string]$ModeOverride = "",
    [string]$MissionJson = "",
    [string]$Preset = "big",
    [string]$ModelDir = "",

    [double]$DvTotalMps = 0,
    [double]$TravelDays = 0,
    [double]$SolarDistanceAu = 0,
    [double]$MinDistanceAu = 0,
    [double]$PayloadMassKg = 0,
    [string]$Origin = "",
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$mode = if ($ModeOverride) { $ModeOverride } else { $Mode }

Write-Host ""
Write-Host "  Rocket Builder"
Write-Host "  =============="
Write-Host ""

switch ($mode.ToLower()) {
    "gui" {
        Write-Host "GUI mode is not available in the WPF version."
        Write-Host "The old WinForms Rocket Builder was removed during the redesign."
        Write-Host ""
        Write-Host "To use the ML predictor from command line:"
        Write-Host "  .\launch_rocket_builder.ps1 -Mode predict ..."
        Write-Host ""
        Write-Host "Note: a trained model (.pt) is required for prediction."
        Write-Host "Train one with: .\rocket_builder_ml\run.ps1 train base"
        Write-Host ""
    }
    "train" {
        & "$scriptRoot\rocket_builder_ml\run.ps1" train -Preset $Preset
    }
    "predict" {
        if (-not [string]::IsNullOrWhiteSpace($MissionJson)) {
            & "$scriptRoot\rocket_builder_ml\run.ps1" predict -Preset $Preset -MissionJson $MissionJson -ModelDir $ModelDir
        }
        elseif ($DvTotalMps -gt 0) {
            $args = @("predict", "-Preset", $Preset, "-DvTotalMps", $DvTotalMps, "-TravelDays", $TravelDays, "-SolarDistanceAu", $SolarDistanceAu, "-PayloadMassKg", $PayloadMassKg)
            if ($MinDistanceAu -gt 0) { $args += "-MinDistanceAu"; $args += $MinDistanceAu }
            if ($Origin) { $args += "-Origin"; $args += $Origin }
            if ($Destination) { $args += "-Destination"; $args += $Destination }
            if ($ModelDir) { $args += "-ModelDir"; $args += $ModelDir }
            & "$scriptRoot\rocket_builder_ml\run.ps1" @args
        }
        else {
            & "$scriptRoot\rocket_builder_ml\run.ps1" help
        }
    }
    default {
        & "$scriptRoot\rocket_builder_ml\run.ps1" help
    }
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
