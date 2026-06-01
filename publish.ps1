# WinMUX Self-Contained Publish Script
# Builds all components to ./publish/ for single-folder deployment

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$PublishDir = Join-Path $PSScriptRoot "publish"

Write-Host "=== WinMUX Publish ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Output: $PublishDir"
Write-Host ""

# Clean previous publish
if (Test-Path $PublishDir) {
    Write-Host "Cleaning previous publish..." -ForegroundColor Yellow
    Remove-Item -Path "$PublishDir\*" -Recurse -Force -ErrorAction SilentlyContinue
}

$null = New-Item -ItemType Directory -Path $PublishDir -Force

# Build flags
$BuildArgs = @(
    "--configuration", $Configuration
    "--runtime", "win-x64"
    "--self-contained", "true"
    "/p:PublishSingleFile=true"
    "/p:PublishTrimmed=false"
    "/p:IncludeNativeLibrariesForSelfExtract=true"
    "/p:DebugType=embedded"
    "--output", $PublishDir
)

if ($NoRestore) {
    $BuildArgs += "--no-restore"
}

$Projects = @(
    "src\WinMUX.Daemon\WinMUX.Daemon.csproj"
    "src\WinMUX.CLI\WinMUX.CLI.csproj"
    "src\WinMUX.Server\WinMUX.Server.csproj"
)

foreach ($proj in $Projects) {
    $ProjName = Split-Path $proj -Leaf
    Write-Host "Publishing $ProjName..." -ForegroundColor Green
    
    dotnet publish $proj @BuildArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to publish $ProjName"
        exit 1
    }
}

Write-Host ""
Write-Host "=== Publish Complete ===" -ForegroundColor Cyan
Write-Host "Location: $PublishDir"
Write-Host ""

$Exes = Get-ChildItem -Path $PublishDir -Filter "WinMUX.*.exe" | Select-Object -ExpandProperty Name
Write-Host "Executables:" -ForegroundColor Green
$Exes | ForEach-Object { Write-Host "  $_" }

Write-Host ""
Write-Host "Run: cd publish; .\WinMUX.CLI.exe daemon start" -ForegroundColor Yellow
