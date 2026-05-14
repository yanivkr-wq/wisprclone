# build-installer.ps1 — publishes WisprClone self-contained and packages it
# into an Inno Setup installer (WisprCloneSetup.exe).
#
# Usage:
#   .\build-installer.ps1
#
# Prerequisites:
#   - .NET 8 SDK   (winget install Microsoft.DotNet.SDK.8)
#   - Inno Setup 6 (winget install JRSoftware.InnoSetup)

$ErrorActionPreference = "Stop"

$root         = $PSScriptRoot
$projPath     = Join-Path $root "src\WisprClone.App\WisprClone.App.csproj"
$publishDir   = Join-Path $root "src\WisprClone.App\bin\Release\publish"
$installerIss = Join-Path $root "installer\WisprClone.iss"

# --- dotnet publish ---
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet not found. Install .NET 8 SDK: winget install Microsoft.DotNet.SDK.8" }

Write-Host "===  Publishing self-contained build → $publishDir" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& $dotnet publish $projPath `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$publishedExe = Join-Path $publishDir "WisprClone.exe"
if (-not (Test-Path $publishedExe)) { throw "Expected $publishedExe after publish, not found." }

$pubSizeMb = [math]::Round((Get-ChildItem $publishDir -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "    Publish folder: $pubSizeMb MB" -ForegroundColor DarkGray

# --- Inno Setup compile ---
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup ISCC.exe not found in any of:`n  $($isccCandidates -join "`n  ")`nInstall: winget install JRSoftware.InnoSetup"
}

Write-Host "===  Compiling installer → installer\Output\WisprCloneSetup.exe" -ForegroundColor Cyan
& $iscc $installerIss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed (exit $LASTEXITCODE)" }

$setupExe = Join-Path $root "installer\Output\WisprCloneSetup.exe"
if (-not (Test-Path $setupExe)) { throw "Expected $setupExe after compile, not found." }

$setupSizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Installer: $setupExe ($setupSizeMb MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Share this .exe with anyone running Windows 10/11 x64. Double-click to install."
Write-Host "First-run UX: the Welcome wizard prompts for their OpenAI API key with links to platform.openai.com."
