# ==============================================================================
# Transfer Window Planner - Easy Launcher
# Automatically checks dependencies and launches the application
# ==============================================================================

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "Transfer Window Planner Launcher"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Transfer Window Planner      " -ForegroundColor Cyan
Write-Host "  Auto Launcher v1.0           " -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Check if .NET SDK is installed
function Test-DotNetSdk {
    try {
        $dotnetVersion = & dotnet --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[OK] .NET SDK found: version $dotnetVersion" -ForegroundColor Green
            return $true
        }
    }
    catch {
        return $false
    }
    return $false
}

# Install .NET SDK
function Install-DotNetSdk {
    Write-Host "[!] .NET SDK not found. Starting installation..." -ForegroundColor Yellow
    Write-Host ""
    
    try {
        Write-Host "Opening .NET download page..." -ForegroundColor Cyan
        
        # Open download page in browser
        Start-Process "https://dotnet.microsoft.com/download/dotnet/10.0"
        
        Write-Host ""
        Write-Host "Browser opened with .NET SDK download page." -ForegroundColor Yellow
        Write-Host "Please:" -ForegroundColor Yellow
        Write-Host "  1. Download and install .NET 10 SDK" -ForegroundColor White
        Write-Host "  2. Press any key to continue after installation" -ForegroundColor White
        Write-Host ""
        
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        
        # Reload environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        
        if (Test-DotNetSdk) {
            Write-Host "[OK] .NET SDK installed successfully!" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "[ERROR] .NET SDK still not detected. May need terminal restart." -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "[ERROR] Installation error: $_" -ForegroundColor Red
        return $false
    }
}

# Build the project
function Build-Project {
    param([string]$ProjectPath)
    
    Write-Host ""
    Write-Host "Building project..." -ForegroundColor Cyan
    
    try {
        $buildOutput = & dotnet build $ProjectPath --configuration Release --verbosity quiet 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[OK] Project built successfully" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "[ERROR] Build failed:" -ForegroundColor Red
            Write-Host $buildOutput -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "[ERROR] Build error: $_" -ForegroundColor Red
        return $false
    }
}

# Launch the application
function Start-Application {
    param([string]$ProjectPath)
    
    Write-Host ""
    Write-Host "Starting Transfer Window Planner..." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "================================" -ForegroundColor Green
    Write-Host ""
    
    try {
        & dotnet run --project $ProjectPath --configuration Release --no-build
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "[ERROR] Application exited with error (code: $LASTEXITCODE)" -ForegroundColor Red
            return $false
        }
        
        return $true
    }
    catch {
        Write-Host ""
        Write-Host "[ERROR] Launch error: $_" -ForegroundColor Red
        return $false
    }
}

# ==============================================================================
# MAIN SCRIPT
# ==============================================================================

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptRoot "TransferWindowPlanner.Wpf\TransferWindowPlanner.Wpf.csproj"

# Check if project exists
if (-not (Test-Path -LiteralPath $projectPath)) {
    Write-Host "[ERROR] Project file not found: $projectPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# Step 1: Check .NET SDK
if (-not (Test-DotNetSdk)) {
    if (-not (Install-DotNetSdk)) {
        Write-Host ""
        Write-Host "Could not install .NET SDK. Please install manually:" -ForegroundColor Red
        Write-Host "https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
}

# Step 2: Build project
if (-not (Build-Project -ProjectPath $projectPath)) {
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# Step 3: Launch application
Start-Application -ProjectPath $projectPath

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Application closed" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
