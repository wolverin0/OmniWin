# OmniWin Distribution Packaging Script
# Compiles, publishes self-contained win-x64, builds ZIP release and calculates SHA256

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "publish\omniwin-sc"
$ZipPath = Join-Path $RepoRoot "publish\omniwin-sc.zip"
$ProjectFile = Join-Path $RepoRoot "OmniWin.UI\OmniWin.UI.csproj"

Write-Host "=== OmniWin v$Version Distribution Build ===" -ForegroundColor Cyan

# 1. Clean previous publish directory
if (Test-Path $PublishDir) {
    Write-Host "Cleaning existing publish directory..." -ForegroundColor Yellow
    Remove-Item -Path $PublishDir -Recurse -Force
}

# 2. Publish self-contained executable
Write-Host "Publishing self-contained win-x64 build..." -ForegroundColor Green
dotnet publish $ProjectFile `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 3. Create Release ZIP
Write-Host "Creating release archive $ZipPath..." -ForegroundColor Green
if (Test-Path $ZipPath) {
    Remove-Item -Path $ZipPath -Force
}

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

# 4. Compute SHA256 Hash
$hash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
Write-Host "Release ZIP created successfully!" -ForegroundColor Green
Write-Host "ZIP Path: $ZipPath" -ForegroundColor White
Write-Host "SHA256:   $hash" -ForegroundColor Yellow

# 5. Update WinGet installer manifest with actual hash
$InstallerYaml = Join-Path $RepoRoot "distribution\winget\pauol.OmniWin.installer.yaml"
if (Test-Path $InstallerYaml) {
    $content = Get-Content $InstallerYaml -Raw
    $content = $content -replace "InstallerSha256: [0-9a-fA-F]{64}", "InstallerSha256: $hash"
    Set-Content -Path $InstallerYaml -Value $content -NoNewline
    Write-Host "Updated WinGet installer manifest with SHA256 hash." -ForegroundColor Cyan
}

Write-Host "=== Build and Distribution Complete! ===" -ForegroundColor Cyan
