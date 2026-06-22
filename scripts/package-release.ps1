param(
    [string]$Version = "v0.8.1-installer-fix",
    [string]$GuiExe = "",
    [string]$BackendExe = "",
    [string]$BackendPdb = "",
    [string]$RoseVExe = "",
    [string]$DevKitDll = "",
    [string]$FriendStarter = "",
    [string]$OutDir = "dist",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

function Resolve-ReleasePath([string]$PathValue, [string[]]$Fallbacks, [string]$Label) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($PathValue)) {
        $candidates += $PathValue
    }

    foreach ($fallback in $Fallbacks) {
        $candidates += (Join-Path $repoRoot $fallback)
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "$Label was not found. Build it first or pass an explicit path."
}

function Copy-IfExists([string]$Source, [string]$Destination) {
    if ([string]::IsNullOrWhiteSpace($Source) -or -not (Test-Path -LiteralPath $Source)) {
        return
    }

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-DirectoryWithoutBuildOutput([string]$Source, [string]$Destination) {
    $resolvedSource = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')
    Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    Get-ChildItem -LiteralPath $resolvedSource -Recurse -Force | ForEach-Object {
        $relative = $_.FullName.Substring($resolvedSource.Length).TrimStart('\')
        if ([string]::IsNullOrWhiteSpace($relative)) {
            return
        }

        $parts = $relative -split '[\\/]'
        if ($parts -contains "bin" -or $parts -contains "obj") {
            return
        }

        $target = Join-Path $Destination $relative
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
        } else {
            $parent = Split-Path -Parent $target
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

$guiPath = Resolve-ReleasePath $GuiExe @(
    "TauriInstaller\src-tauri\target\release\meloncompat-installer-tauri.exe"
) "Tauri GUI executable"

$backendPath = Resolve-ReleasePath $BackendExe @(
    "TauriInstaller\backend\MelonCompatInstaller.exe",
    "dist\installer\MelonCompatInstaller.exe",
    "Installer\bin\Release\net8.0\win-x64\publish\MelonCompatInstaller.exe",
    "Installer\bin\Release\net8.0\win-x64\MelonCompatInstaller.exe"
) "CLI backend executable"

$guiItem = Get-Item -LiteralPath $guiPath
$backendItem = Get-Item -LiteralPath $backendPath

if ($guiItem.FullName -eq $backendItem.FullName) {
    throw "The GUI executable and backend executable resolve to the same file. Refusing to package a broken release."
}

if ($guiItem.Length -gt 50MB) {
    throw "The GUI executable is larger than expected. This usually means the CLI backend was passed as the GUI."
}

if ($backendItem.Length -lt 20MB) {
    throw "The backend executable is smaller than expected. This usually means the Tauri GUI was passed as the backend."
}

$safeVersion = $Version.Trim()
if ([string]::IsNullOrWhiteSpace($safeVersion)) {
    throw "Version cannot be blank."
}

$releaseName = "MelonCompat-RoseMod-$safeVersion"
$outputRoot = if ([IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $repoRoot $OutDir }
$releaseRoot = Join-Path $outputRoot $releaseName
$zipPath = Join-Path $outputRoot "$releaseName.zip"

Remove-Item -LiteralPath $releaseRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "backend") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "cli") | Out-Null

Copy-Item -LiteralPath $guiPath -Destination (Join-Path $releaseRoot "MelonCompat Installer.exe") -Force
Copy-Item -LiteralPath $backendPath -Destination (Join-Path $releaseRoot "backend\MelonCompatInstaller.exe") -Force
Copy-Item -LiteralPath $backendPath -Destination (Join-Path $releaseRoot "cli\MelonCompatInstaller.exe") -Force

$resolvedBackendPdb = $BackendPdb
if ([string]::IsNullOrWhiteSpace($resolvedBackendPdb)) {
    $possiblePdb = [IO.Path]::ChangeExtension($backendPath, ".pdb")
    if (Test-Path -LiteralPath $possiblePdb) {
        $resolvedBackendPdb = $possiblePdb
    }
}
Copy-IfExists $resolvedBackendPdb (Join-Path $releaseRoot "backend\MelonCompatInstaller.pdb")
Copy-IfExists $resolvedBackendPdb (Join-Path $releaseRoot "cli\MelonCompatInstaller.pdb")

$readme = @"
# MelonCompat and RoseMod $safeVersion

Run `MelonCompat Installer.exe` for the graphical installer.

The `backend` folder is used by the GUI. Do not run files inside it unless you are debugging.

For command-line installs, use `cli\MelonCompatInstaller.exe`.

Quick checks:

- The top-level GUI is named `MelonCompat Installer.exe`.
- The CLI backend is under `backend\` and `cli\`.
- RoseMod installs to a game's `RoseMod` folder.
- MelonCompat installs to a game's `BepInEx\plugins` folder.
"@

Set-Content -LiteralPath (Join-Path $releaseRoot "README_RELEASE.md") -Value $readme -Encoding UTF8

if (Test-Path -LiteralPath (Join-Path $repoRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $releaseRoot "README.md") -Force
}

if (Test-Path -LiteralPath (Join-Path $repoRoot "docs\assets")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "docs") | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\assets") -Destination (Join-Path $releaseRoot "docs\assets") -Recurse -Force
}

$friendSource = $FriendStarter
if ([string]::IsNullOrWhiteSpace($friendSource)) {
    $candidate = Join-Path $repoRoot "dist\RoseV-Friend-Starter"
    if (Test-Path -LiteralPath $candidate) {
        $friendSource = $candidate
    } else {
        $candidate = Join-Path $repoRoot "Samples\RoseV"
        if (Test-Path -LiteralPath $candidate) {
            $friendSource = $candidate
        }
    }
}
if (-not [string]::IsNullOrWhiteSpace($friendSource) -and (Test-Path -LiteralPath $friendSource)) {
    Copy-DirectoryWithoutBuildOutput $friendSource (Join-Path $releaseRoot "friend-starter")
}

$toolRoot = Join-Path $releaseRoot "tools"
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null

$roseVPath = $RoseVExe
if ([string]::IsNullOrWhiteSpace($roseVPath)) {
    $candidate = Join-Path $repoRoot "bin\RoseV\Release\RoseV.exe"
    if (Test-Path -LiteralPath $candidate) {
        $roseVPath = $candidate
    }
}
Copy-IfExists $roseVPath (Join-Path $toolRoot "RoseV.exe")

$devKitPath = $DevKitDll
if ([string]::IsNullOrWhiteSpace($devKitPath)) {
    $candidate = Join-Path $repoRoot "bin\RoseMod\DevKit\Release\netstandard2.0\RoseMod.DevKit.dll"
    if (Test-Path -LiteralPath $candidate) {
        $devKitPath = $candidate
    }
}
Copy-IfExists $devKitPath (Join-Path $toolRoot "RoseMod.DevKit.dll")

if (-not $NoZip) {
    Compress-Archive -Path $releaseRoot -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host "Packaged release folder: $releaseRoot"
if (-not $NoZip) {
    Write-Host "Packaged release zip: $zipPath"
}
