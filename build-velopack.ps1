# build-velopack.ps1 — packages WisprClone via Velopack and (optionally)
# uploads the resulting release to GitHub Releases for OTA delivery.
#
# Usage:
#   .\build-velopack.ps1                 # just build, don't upload
#   .\build-velopack.ps1 -Publish        # also upload to GitHub Releases
#   .\build-velopack.ps1 -Version 0.3.0  # override the version
#
# Prerequisites (one-time):
#   dotnet tool install -g vpk
#   winget install GitHub.cli
#   gh auth login

param(
    [string] $Version = "",
    [string] $RepoUrl = "",
    [switch] $Publish
)

$ErrorActionPreference = "Stop"

$root        = $PSScriptRoot
$projPath    = Join-Path $root "src\WisprClone.App\WisprClone.App.csproj"
$publishDir  = Join-Path $root "src\WisprClone.App\bin\Release\publish"
$releasesDir = Join-Path $root "releases"

# --- Resolve version ---
if (-not $Version) {
    # Read from csproj if not specified.
    [xml]$csproj = Get-Content $projPath
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
    if (-not $Version) { $Version = "0.0.0" }
}
Write-Host "===  Building WisprClone v$Version" -ForegroundColor Cyan

# --- Resolve dotnet ---
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) { $dotnet = $dotnetCmd.Source } else { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet not found. Install .NET 8 SDK: winget install Microsoft.DotNet.SDK.8" }

# --- Resolve vpk ---
$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if ($vpk) { $vpk = $vpk.Source } else { $vpk = "$env:USERPROFILE\.dotnet\tools\vpk.exe" }
if (-not (Test-Path $vpk)) { throw "vpk not found. Install: dotnet tool install -g vpk" }

# --- 1. Publish self-contained build ---
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "===  Publishing self-contained build → $publishDir" -ForegroundColor Cyan
& $dotnet publish $projPath `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# --- 2. Package via Velopack ---
if (-not (Test-Path $releasesDir)) { New-Item -Path $releasesDir -ItemType Directory | Out-Null }

Write-Host "===  Packing with vpk → $releasesDir" -ForegroundColor Cyan
& $vpk pack `
    --packId WisprClone `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe WisprClone.exe `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

# vpk 0.0.1298 names the installer "WisprClone-win-Setup.exe" (with the RID
# in the filename). Older vpk versions used "WisprClone-Setup.exe". Probe both.
$setupExe = (Get-ChildItem $releasesDir -Filter "WisprClone*Setup.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if (-not $setupExe -or -not (Test-Path $setupExe)) { throw "No WisprClone*Setup.exe in $releasesDir after vpk pack." }

$setupSizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Installer: $setupExe ($setupSizeMb MB)" -ForegroundColor Green

# --- 3. Optional: upload to GitHub Releases ---
if ($Publish) {
    if (-not $RepoUrl) {
        $ghCmd0 = Get-Command gh -ErrorAction SilentlyContinue
        if ($ghCmd0) { $ghProbe = $ghCmd0.Source } else { $ghProbe = "C:\Program Files\GitHub CLI\gh.exe" }
        if (-not (Test-Path $ghProbe)) { throw "gh CLI not found. Install: winget install GitHub.cli" }
        $repoNwo = (& $ghProbe repo view --json nameWithOwner --jq .nameWithOwner) 2>$null
        if (-not $repoNwo) { throw "Could not detect GitHub repo. Pass -RepoUrl or run from within a gh-detected repo." }
        $RepoUrl = "https://github.com/$repoNwo"
    }

    # vpk's own upload tries to spawn powershell internally, which the Claude
    # sandbox blocks. Use gh CLI directly — same effect: artifacts uploaded to
    # a GitHub Release at the requested tag.
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) { $gh = $ghCmd.Source } else { $gh = "C:\Program Files\GitHub CLI\gh.exe" }
    if (-not (Test-Path $gh)) { throw "gh CLI not found. Install: winget install GitHub.cli" }

    # Strip the protocol + host to get "owner/repo"
    $nwo = $RepoUrl -replace "^https?://github\.com/", ""

    Write-Host ""
    Write-Host "===  Uploading to $RepoUrl Releases (tag v$Version) via gh" -ForegroundColor Cyan

    $assets = Get-ChildItem $releasesDir -File `
        | Where-Object { $_.Extension -in @(".exe", ".nupkg", ".zip") -or $_.Name -in @("RELEASES", "releases.win.json", "assets.win.json") } `
        | ForEach-Object { $_.FullName }

    if ($assets.Count -eq 0) { throw "No release artifacts found in $releasesDir" }

    $notes = "WisprClone $Version. Existing installs auto-update via the tray menu within 4 hours, or click 'Check for updates…' to apply now."
    $cmdArgs = @("release", "create", "v$Version", "--repo", $nwo, "--title", "WisprClone $Version", "--notes", $notes) + $assets
    & $gh @cmdArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed (exit $LASTEXITCODE)" }

    Write-Host ""
    Write-Host "Release v$Version published to $RepoUrl/releases" -ForegroundColor Green
    Write-Host "Existing installs will discover this version on their next periodic check (within 4h)" -ForegroundColor Green
    Write-Host "or when the user clicks 'Check for updates…' in the tray menu." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Skipped GitHub upload (no -Publish flag)." -ForegroundColor DarkGray
    Write-Host "To publish: .\build-velopack.ps1 -Publish" -ForegroundColor DarkGray
}
