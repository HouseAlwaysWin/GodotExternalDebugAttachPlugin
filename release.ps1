# Release Script for External Debug Attach Plugin
# Usage: .\release.ps1 -Version "2.0.0"

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [switch]$SkipBuild,
    [switch]$SkipGitHubRelease
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Release v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build and publish Debug Attach Service
if (-not $SkipBuild) {
    Write-Host "[1/5] Building Debug Attach Service..." -ForegroundColor Yellow
    Push-Location "DebugAttachService"
    dotnet publish -c Release -o "..\ExternalDebugAttachPlugin\addons\external_debug_attach\bin"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Pop-Location
    Write-Host "Build complete." -ForegroundColor Green
} else {
    Write-Host "[1/5] Skipping build..." -ForegroundColor Gray
}

# Step 2: Git add all changes
Write-Host ""
Write-Host "[2/5] Staging changes..." -ForegroundColor Yellow
git add .
Write-Host "Changes staged." -ForegroundColor Green

# Step 3: Git commit
Write-Host ""
Write-Host "[3/5] Committing..." -ForegroundColor Yellow
git commit -m "release: v$Version - GDScript plugin + Debug Attach Service"
Write-Host "Committed." -ForegroundColor Green

# Step 4: Create tag and push
Write-Host ""
Write-Host "[4/5] Creating tag and pushing..." -ForegroundColor Yellow
git tag "v$Version"
git push origin master
git push origin "v$Version"
Write-Host "Pushed to origin." -ForegroundColor Green

# Step 5: Create GitHub Release (requires GitHub CLI)
if (-not $SkipGitHubRelease) {
    Write-Host ""
    Write-Host "[5/5] Creating GitHub Release..." -ForegroundColor Yellow
    
    $ghExists = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghExists) {
        gh release create "v$Version" --title "v$Version" --notes-file CHANGELOG.md
        Write-Host "GitHub Release created." -ForegroundColor Green
    } else {
        Write-Host "GitHub CLI not installed. Please create release manually:" -ForegroundColor Yellow
        Write-Host "  1. Go to: https://github.com/YOUR_USERNAME/GodotExternalDebugAttachPlugin/releases/new" -ForegroundColor White
        Write-Host "  2. Tag: v$Version" -ForegroundColor White
        Write-Host "  3. Copy CHANGELOG.md content as release notes" -ForegroundColor White
    }
} else {
    Write-Host ""
    Write-Host "[5/5] Skipping GitHub Release..." -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Release v$Version Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
