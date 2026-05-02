# Release script — build addon zip, extract CHANGELOG section for GitHub Release notes, commit/tag/push.
# Usage:
#   .\release.ps1 -Version "2.1.2"
#   .\release.ps1 -Version "2.1.2" -SkipCommit      # tag already prepared
#   .\release.ps1 -Version "2.1.2" -SkipGitHubRelease
#
# Prerequisites: git, dotnet 8 SDK, GitHub CLI (`gh`) for releases.
# Before running: add a section to CHANGELOG.md:
#   ## [2.1.2] - 2026-05-02
#   ### Added
#   ...

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$SkipBuild,
    [switch]$SkipCommit,
    [switch]$SkipGitHubRelease
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot

# Normalize tag: always vX.Y.Z
$Tag = if ($Version -match '^\s*v[\d]') { $Version.Trim() } else { "v$($Version.Trim())" }
$VerPlain = $Tag.TrimStart('v')

function Get-ReleaseNotesFromChangelog {
    param(
        [Parameter(Mandatory)][string]$ChangelogPath,
        [Parameter(Mandatory)][string]$VersionPlain
    )

    if (-not (Test-Path -LiteralPath $ChangelogPath)) {
        return $null
    }

    $escaped = [regex]::Escape($VersionPlain)
    # ## [1.2.3] or ## [v1.2.3] — optional date after ]
    $headerRegex = "^##\s+\[(?:v)?$escaped\](?:\s+-\s+.*)?\s*$"

    $lines = Get-Content -LiteralPath $ChangelogPath -Encoding UTF8
    $started = $false
    $buf = [System.Collections.Generic.List[string]]::new()

    foreach ($line in $lines) {
        if (-not $started) {
            if ($line -match $headerRegex) {
                $started = $true
            }
            continue
        }

        if ($line -match '^##\s+') {
            break
        }

        [void]$buf.Add($line)
    }

    if (-not $started) {
        return $null
    }

    while ($buf.Count -gt 0 -and [string]::IsNullOrWhiteSpace($buf[0])) {
        $buf.RemoveAt(0)
    }
    while ($buf.Count -gt 0 -and [string]::IsNullOrWhiteSpace($buf[$buf.Count - 1])) {
        $buf.RemoveAt($buf.Count - 1)
    }

    if ($buf.Count -eq 0) {
        return "(No bullet points under this version — edit CHANGELOG.md.)"
    }

    return ($buf -join "`n").TrimEnd()
}

function Get-GitHubBlobUrl {
    try {
        $remote = git remote get-url origin 2>$null
        if (-not $remote) { return $null }
        if ($remote -match 'github\.com[:/]([^/]+)/([^/.]+)') {
            $owner = $Matches[1]
            $repo = $Matches[2] -replace '\.git$', ''
            $branch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
            if (-not $branch) { $branch = "master" }
            return "https://github.com/$owner/$repo/blob/$branch/CHANGELOG.md"
        }
    }
    catch { }
    return $null
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Release $Tag" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$addonRoot = Join-Path $RepoRoot "ExternalDebugAttachPlugin\addons\external_debug_attach"
$publishOut = Join-Path $addonRoot "bin"
$zipName = "external_debug_attach_plugin.zip"
$zipPath = Join-Path $RepoRoot $zipName

# --- Build (same RID as CI — Windows Godot users) ---
if (-not $SkipBuild) {
    Write-Host "[1/6] Publishing Debug Attach Service (win-x64)..." -ForegroundColor Yellow
    $csproj = Join-Path $RepoRoot "DebugAttachService\DebugAttachService.csproj"
    dotnet publish $csproj `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -o $publishOut
    if ($LASTEXITCODE -ne 0) {
        Write-Host "dotnet publish failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "Publish complete -> $publishOut" -ForegroundColor Green
}
else {
    Write-Host "[1/6] Skipping build..." -ForegroundColor Gray
}

# --- Zip: folder `external_debug_attach` at archive root (Asset Library / README layout) ---
Write-Host ""
Write-Host "[2/6] Creating release zip..." -ForegroundColor Yellow
if (-not (Test-Path -LiteralPath $addonRoot)) {
    Write-Host "Addon folder missing: $addonRoot" -ForegroundColor Red
    exit 1
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$addonsParent = Split-Path -Parent $addonRoot
$folderName = Split-Path -Leaf $addonRoot
Push-Location $addonsParent
try {
    Compress-Archive -Path $folderName -DestinationPath $zipPath -Force -CompressionLevel Optimal
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $zipPath)) {
    Write-Host "Zip failed: $zipPath" -ForegroundColor Red
    exit 1
}

$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "Zip ready: $zipName ($zipMb MB)" -ForegroundColor Green

# --- CHANGELOG section for GitHub release body ---
Write-Host ""
Write-Host "[3/6] Resolving release notes from CHANGELOG.md..." -ForegroundColor Yellow
$changelogPath = Join-Path $RepoRoot "CHANGELOG.md"
$notesBody = Get-ReleaseNotesFromChangelog -ChangelogPath $changelogPath -VersionPlain $VerPlain
if (-not $notesBody) {
    $blob = Get-GitHubBlobUrl
    $hint = if ($blob) { "Full history: $blob" } else { "See CHANGELOG.md in the repository." }
    Write-Host "WARNING: No section matching ``## [$VerPlain]`` in CHANGELOG.md." -ForegroundColor Yellow
    Write-Host "         Add it before releasing. Using short fallback for GitHub notes." -ForegroundColor Yellow
    $notesBody = @"
## $Tag

No ``## [$VerPlain]`` section found in CHANGELOG.md — add release notes there and re-run, or edit the GitHub release manually.

$hint
"@
}
else {
    # Title line optional — gh shows tag as title; body is the section content
    Write-Host "Found CHANGELOG section for $VerPlain." -ForegroundColor Green
}

$notesFile = Join-Path $env:TEMP "godot-external-debug-attach-release-notes-$VerPlain.md"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($notesFile, $notesBody, $utf8NoBom)

# --- Git commit / tag / push ---
if (-not $SkipCommit) {
    Write-Host ""
    Write-Host "[4/6] Git commit (if needed)..." -ForegroundColor Yellow
    git add -A
    git diff --cached --quiet | Out-Null
    # git exits 1 when there are staged differences
    if ($LASTEXITCODE -eq 1) {
        git commit -m "release: $Tag"
        Write-Host "Committed." -ForegroundColor Green
    }
    else {
        Write-Host "Nothing to commit (working tree clean)." -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "[5/6] Tag + push..." -ForegroundColor Yellow
    # Delete local tag if re-run
    $existing = git tag -l $Tag
    if ($existing) {
        git tag -d $Tag 2>$null
    }
    git tag $Tag
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    git push origin $branch
    git push origin $Tag
    Write-Host "Pushed branch + $Tag." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "[4-5/6] Skipping commit/tag/push (-SkipCommit)." -ForegroundColor Gray
}

# --- GitHub Release with attachment ---
if (-not $SkipGitHubRelease) {
    Write-Host ""
    Write-Host "[6/6] GitHub Release + attach zip..." -ForegroundColor Yellow
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        Write-Host "GitHub CLI (gh) not found. Install: https://cli.github.com/" -ForegroundColor Red
        Write-Host "Zip is at: $zipPath" -ForegroundColor Yellow
        exit 1
    }

    if ($SkipCommit) {
        # Tag must exist on remote for gh release create
        Write-Host "Using existing tag (ensure $Tag exists on origin)." -ForegroundColor Gray
    }

    gh release create $Tag `
        --title $Tag `
        --notes-file $notesFile `
        $zipPath

    if ($LASTEXITCODE -ne 0) {
        Write-Host "gh release create failed. If release already exists: gh release delete $Tag --yes (remote) then retry, or upload zip manually." -ForegroundColor Red
        Write-Host "Zip path: $zipPath" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "GitHub Release created with $zipName attached." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "[6/6] Skipping GitHub Release (-SkipGitHubRelease)." -ForegroundColor Gray
    Write-Host "Zip path: $zipPath" -ForegroundColor White
}

Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Release $Tag complete." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
