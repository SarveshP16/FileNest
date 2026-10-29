# Builds the FileNest installer (Setup.exe) with Velopack, and optionally publishes it as an update.
#   .\build-installer.ps1 -Version 1.0.1
#   .\build-installer.ps1 -Version 1.0.1 -ReleaseNotes notes.md -Publish
# Output: artifacts\installer\FileNest-win-Setup.exe plus the release files the in-app updater reads.
# -Publish uploads a GitHub release to $RepoUrl (must be a public repo; uses GITHUB_TOKEN or your `gh` login).
param(
    [string]$Version = "1.0.0",
    [string]$ReleaseNotes = "",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# Must match AppUpdater.RepoUrl in src\FileNest\Services\AppUpdater.cs.
$RepoUrl = "https://github.com/SarveshP16/FileNest"

# The .NET SDK may be installed per-user rather than machine-wide; prefer that copy when it exists.
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (Test-Path $dotnet) { $env:DOTNET_ROOT = Split-Path $dotnet } else { $dotnet = "dotnet" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$publishDir = "artifacts\publish"
$installerDir = "artifacts\installer"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

& $dotnet tool restore

if ($Publish) {
    $token = $env:GITHUB_TOKEN
    if (-not $token -and (Get-Command gh -ErrorAction SilentlyContinue)) { $token = (gh auth token) }
    if (-not $token) { throw "Set GITHUB_TOKEN or sign in with 'gh auth login' to publish." }

    # Fetch the latest published release so vpk can make a small delta update from it.
    # (Fails harmlessly when nothing has been published yet.)
    & $dotnet vpk download github --repoUrl $RepoUrl --token $token --outputDir $installerDir
}

& $dotnet publish src\FileNest -c Release -r win-x64 --self-contained false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# --framework makes Setup.exe install the .NET Desktop Runtime first if the PC doesn't have it.
$packArgs = @(
    "vpk", "pack",
    "--packId", "FileNest",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "FileNest.exe",
    "--packTitle", "FileNest",
    "--packAuthors", "FileNest",
    "--icon", "src\FileNest\Assets\FileNest.ico",
    "--framework", "net10-x64-desktop",
    "--outputDir", $installerDir
)
if ($ReleaseNotes) { $packArgs += @("--releaseNotes", $ReleaseNotes) }
& $dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

if ($Publish) {
    & $dotnet vpk upload github --repoUrl $RepoUrl --token $token --publish `
        --releaseName "FileNest $Version" --tag "v$Version" --outputDir $installerDir
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }
    Write-Host "Published FileNest $Version to $RepoUrl/releases"
}

Get-ChildItem $installerDir | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
