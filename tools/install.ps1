#Requires -Version 5.1
<#
.SYNOPSIS
    Data Center Modloader - Fully Automatic Installer

.DESCRIPTION
    This script:
    1. Automatically finds where Data Center is installed (searches all Steam libraries)
    2. Downloads and installs MelonLoader v0.7.2 if not already present
    3. Builds the Rust mods and C# plugin
    4. Copies everything to the game directory

.PARAMETER GamePath
    Optional override for the game path. If not specified, the script will
    search all Steam library folders automatically.

.PARAMETER SkipBuild
    Skip building the Rust and C# projects (just copy existing build output).

.PARAMETER SkipMelonLoader
    Skip MelonLoader installation even if it is not detected.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -GamePath "D:\Games\Data Center"
    .\install.ps1 -SkipBuild
#>

param(
    [string]$GamePath = "",
    [switch]$SkipBuild,
    [switch]$SkipMelonLoader
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ============================================================================
# Configuration
# ============================================================================

$GAME_NAME            = "Data Center"
$GAME_EXE             = "Data Center.exe"
$GAME_FOLDER_NAME     = "Data Center"

$MELONLOADER_VERSION  = "v0.7.2"
$MELONLOADER_ZIP_URL  = "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.2/MelonLoader.x64.zip"
$MELONLOADER_ZIP_NAME = "MelonLoader.x64.zip"

# Resolve project root (parent of tools/)
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectRoot = Split-Path -Parent $ScriptDir

# ============================================================================
# Helper Functions
# ============================================================================

function Write-Banner {
    Write-Host ""
    Write-Host "  ============================================" -ForegroundColor Cyan
    Write-Host "    Data Center Modloader -- Auto Installer"    -ForegroundColor Cyan
    Write-Host "  ============================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([string]$Message)
    Write-Host ("  [>] " + $Message) -ForegroundColor White
}

function Write-Ok {
    param([string]$Message)
    Write-Host ("  [OK] " + $Message) -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host ("  [!!] " + $Message) -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host ("  [FAIL] " + $Message) -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host ("  [i] " + $Message) -ForegroundColor Gray
}

# ============================================================================
# Step 1: Find Steam and all library folders
# ============================================================================

function Find-SteamLibraryFolders {
    <#
    .SYNOPSIS
        Finds all Steam library folders by reading libraryfolders.vdf
        and scanning common locations.
    #>

    $steamPaths = @()

    # Common Steam install locations
    $candidates = @(
        (Join-Path $env:ProgramFiles "Steam")
    )

    # ProgramFiles(x86) may not exist on all systems
    $pf86 = [System.Environment]::GetFolderPath("ProgramFilesX86")
    if ($pf86) {
        $candidates += (Join-Path $pf86 "Steam")
    }

    $candidates += @(
        (Join-Path $env:LOCALAPPDATA "Steam"),
        "C:\Steam",
        "D:\Steam",
        "E:\Steam",
        "F:\Steam"
    )

    # Also check registry for Steam install path
    $regPaths = @(
        "HKLM:\SOFTWARE\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKCU:\SOFTWARE\Valve\Steam"
    )

    foreach ($reg in $regPaths) {
        try {
            $regItem = Get-ItemProperty -Path $reg -ErrorAction SilentlyContinue
            if ($regItem -and $regItem.InstallPath) {
                $steamDir = $regItem.InstallPath
                if (Test-Path $steamDir) {
                    $candidates += $steamDir
                }
            }
        }
        catch {
            # Registry key does not exist, skip
        }
    }

    # Find the actual Steam installation by looking for steam.exe
    $steamRoot = $null
    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) {
            continue
        }
        $steamExeLower = Join-Path $candidate "steam.exe"
        $steamExeUpper = Join-Path $candidate "Steam.exe"
        if ((Test-Path $steamExeLower) -or (Test-Path $steamExeUpper)) {
            $steamRoot = $candidate
            break
        }
    }

    if (-not $steamRoot) {
        Write-Warn "Could not find Steam installation automatically."
        return @()
    }

    Write-Info ("Steam found at: " + $steamRoot)

    # The default library is always steamapps/ under the Steam root
    $defaultLib = Join-Path $steamRoot "steamapps"
    if (Test-Path $defaultLib) {
        $steamPaths += $defaultLib
    }

    # Parse libraryfolders.vdf for additional library paths
    $vdfPath = Join-Path $defaultLib "libraryfolders.vdf"
    if (Test-Path $vdfPath) {
        $vdfContent = Get-Content $vdfPath -Raw

        # Match "path" entries -- handles both old and new VDF format
        # New format: "path"    "D:\\SteamLibrary"
        $pathMatches = [regex]::Matches($vdfContent, '"path"\s+"([^"]+)"')

        foreach ($match in $pathMatches) {
            $libPath = $match.Groups[1].Value -replace '\\\\', '\'
            try {
                if (-not (Test-Path -IsValid $libPath)) { continue }
                $steamAppsPath = Join-Path $libPath "steamapps"
                if ((Test-Path $steamAppsPath) -and ($steamPaths -notcontains $steamAppsPath)) {
                    $steamPaths += $steamAppsPath
                }
            }
            catch {
                # Skip invalid paths (e.g. drives that don't exist)
                continue
            }
        }
    }

    # Also scan all drive letters for common Steam library locations
    $drives = Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -match '^[A-Z]:\\$' }
    foreach ($drive in $drives) {
        $driveRoot = $drive.Root
        $extraPaths = @(
            (Join-Path $driveRoot "SteamLibrary\steamapps"),
            (Join-Path $driveRoot "Steam\steamapps"),
            (Join-Path $driveRoot "Games\Steam\steamapps"),
            (Join-Path $driveRoot "Games\SteamLibrary\steamapps")
        )
        foreach ($p in $extraPaths) {
            if ((Test-Path $p) -and ($steamPaths -notcontains $p)) {
                $steamPaths += $p
            }
        }
    }

    return $steamPaths
}

# ============================================================================
# Step 2: Find Data Center in Steam libraries
# ============================================================================

function Find-GamePath {
    <#
    .SYNOPSIS
        Searches all Steam library folders for the Data Center game.
    #>

    Write-Step ("Searching for '" + $GAME_NAME + "' installation...")

    $libraries = Find-SteamLibraryFolders

    if ($libraries.Count -eq 0) {
        Write-Fail "No Steam library folders found."
        return $null
    }

    $libCount = $libraries.Count
    Write-Info ($libCount.ToString() + " Steam library folder(s) to search.")

    foreach ($lib in $libraries) {
        $gamePath = Join-Path $lib ("common\" + $GAME_FOLDER_NAME)
        $gameExe  = Join-Path $gamePath $GAME_EXE

        if (Test-Path $gameExe) {
            Write-Ok ("Found '" + $GAME_NAME + "' at: " + $gamePath)
            return $gamePath
        }
    }

    # Also check if user has the game in a non-standard location via appmanifest
    foreach ($lib in $libraries) {
        $manifests = Get-ChildItem -Path $lib -Filter "appmanifest_*.acf" -ErrorAction SilentlyContinue
        foreach ($manifest in $manifests) {
            $content = Get-Content $manifest.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -match '"name"\s+"Data Center"') {
                # Extract installdir
                if ($content -match '"installdir"\s+"([^"]+)"') {
                    $installDir = $Matches[1]
                    $gamePath = Join-Path $lib ("common\" + $installDir)
                    if (Test-Path (Join-Path $gamePath $GAME_EXE)) {
                        Write-Ok ("Found '" + $GAME_NAME + "' at: " + $gamePath)
                        return $gamePath
                    }
                }
            }
        }
    }

    return $null
}

# ============================================================================
# Step 3: Install MelonLoader
# ============================================================================

function Install-MelonLoader {
    param([string]$TargetGamePath)

    $melonDir = Join-Path $TargetGamePath "MelonLoader"

    # Check if already installed
    if (Test-Path $melonDir) {
        Write-Ok "MelonLoader is already installed."

        # Quick version check
        $bootstrapDll = Join-Path $TargetGamePath "version.dll"
        if (Test-Path $bootstrapDll) {
            Write-Info "Bootstrap DLL (version.dll) present."
        }
        return $true
    }

    if ($SkipMelonLoader) {
        Write-Warn "MelonLoader not found, but -SkipMelonLoader was specified."
        return $false
    }

    Write-Step ("MelonLoader not found. Downloading " + $MELONLOADER_VERSION + "...")

    $tempDir  = Join-Path $env:TEMP "dc_modloader_install"
    $zipPath  = Join-Path $tempDir $MELONLOADER_ZIP_NAME

    # Create temp directory
    if (-not (Test-Path $tempDir)) {
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    }

    # Download MelonLoader
    try {
        Write-Info ("Downloading from: " + $MELONLOADER_ZIP_URL)
        Write-Info "This may take a moment (~17 MB)..."

        Invoke-WebRequest -Uri $MELONLOADER_ZIP_URL -OutFile $zipPath -UseBasicParsing

        if (-not (Test-Path $zipPath)) {
            Write-Fail "Download failed -- file not found after download."
            return $false
        }

        $fileSizeBytes = (Get-Item $zipPath).Length
        $fileSizeMB = [math]::Round($fileSizeBytes / 1MB, 1)
        Write-Ok ("Downloaded MelonLoader (" + $fileSizeMB.ToString() + " MB)")
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Fail ("Failed to download MelonLoader: " + $errMsg)
        Write-Info "You can manually download it from:"
        Write-Info ("  https://github.com/LavaGang/MelonLoader/releases/tag/" + $MELONLOADER_VERSION)
        return $false
    }

    # Extract to game directory
    try {
        Write-Step "Extracting MelonLoader to game directory..."

        # Extract to temp first, then copy
        $extractDir = Join-Path $tempDir "extracted"
        if (Test-Path $extractDir) {
            Remove-Item -Recurse -Force $extractDir
        }

        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

        # The zip contains a MelonLoader/ folder and some root files (version.dll, etc.)
        # Copy everything to the game directory
        $items = Get-ChildItem -Path $extractDir
        foreach ($item in $items) {
            $destPath = Join-Path $TargetGamePath $item.Name
            if ($item.PSIsContainer) {
                if (Test-Path $destPath) {
                    # Merge directories
                    $srcGlob = Join-Path $item.FullName "*"
                    Copy-Item -Path $srcGlob -Destination $destPath -Recurse -Force
                }
                else {
                    Copy-Item -Path $item.FullName -Destination $destPath -Recurse -Force
                }
            }
            else {
                Copy-Item -Path $item.FullName -Destination $destPath -Force
            }
        }

        Write-Ok ("MelonLoader " + $MELONLOADER_VERSION + " installed successfully!")

        # Verify
        if (Test-Path (Join-Path $TargetGamePath "MelonLoader")) {
            Write-Info "MelonLoader directory created."
        }
        if (Test-Path (Join-Path $TargetGamePath "version.dll")) {
            Write-Info "Bootstrap (version.dll) installed."
        }
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Fail ("Failed to extract MelonLoader: " + $errMsg)
        return $false
    }
    finally {
        # Cleanup
        if (Test-Path $tempDir) {
            Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
        }
    }

    # Create the Mods directory if it does not exist
    $modsDir = Join-Path $TargetGamePath "Mods"
    if (-not (Test-Path $modsDir)) {
        New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
        Write-Info "Created Mods/ directory."
    }

    # Remind user about first launch
    Write-Host ""
    Write-Warn "IMPORTANT: You need to launch the game ONCE for MelonLoader to"
    Write-Warn "generate the Il2CppAssemblies. The first launch will take longer."
    Write-Warn "After that, close the game and run this script again to install"
    Write-Warn "the modloader plugin."
    Write-Host ""

    return $true
}

# ============================================================================
# Step 4: Build projects
# ============================================================================

function Build-Projects {
    param([string]$TargetGamePath)

    if ($SkipBuild) {
        Write-Info "Skipping build (-SkipBuild specified)."
        return $true
    }

    $success = $true

    # Build Rust mods
    Write-Step "Building Rust mods..."
    try {
        Push-Location $ProjectRoot
        $cargoOutput = cmd /c "cargo build --release 2>&1"
        $exitCode = $LASTEXITCODE
        Pop-Location

        if ($exitCode -eq 0) {
            Write-Ok "Rust mods built successfully."
        }
        else {
            Write-Fail "Rust build failed (exit code $exitCode):"
            foreach ($line in $cargoOutput) {
                Write-Info ("  " + $line)
            }
            $success = $false
        }
    }
    catch {
        $errMsg = $_.Exception.Message
        Pop-Location -ErrorAction SilentlyContinue
        Write-Fail ("Failed to run cargo: " + $errMsg)
        Write-Info "Make sure Rust is installed: https://rustup.rs"
        $success = $false
    }

    # Build C# plugin
    Write-Step "Building C# MelonLoader plugin..."

    # Check if Il2CppAssemblies exist (required for C# build)
    $il2cppAssemblies = Join-Path $TargetGamePath "MelonLoader\Il2CppAssemblies"
    if (-not (Test-Path $il2cppAssemblies)) {
        Write-Warn ("Il2CppAssemblies not found at: " + $il2cppAssemblies)
        Write-Warn "You need to launch the game once with MelonLoader first!"
        Write-Warn "Skipping C# build for now."
        return $success
    }

    $csprojPath = Join-Path $ProjectRoot "csharp\DataCenterModLoader\DataCenterModLoader.csproj"
    if (-not (Test-Path $csprojPath)) {
        Write-Warn ("C# project not found at: " + $csprojPath)
        return $success
    }

    try {
        $output = & dotnet build $csprojPath -c Release 2>&1
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            Write-Ok "C# plugin built successfully."
        }
        else {
            Write-Fail "C# build failed:"
            foreach ($line in $output) {
                Write-Info ("  " + $line)
            }
            $success = $false
        }
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Fail ("Failed to run dotnet: " + $errMsg)
        Write-Info "Make sure .NET SDK 6.0+ is installed: https://dotnet.microsoft.com/download"
        $success = $false
    }

    return $success
}

# ============================================================================
# Step 5: Deploy mod files
# ============================================================================

function Deploy-ModFiles {
    param([string]$TargetGamePath)

    Write-Step "Deploying modloader files..."

    $modsDir     = Join-Path $TargetGamePath "Mods"
    $rustModsDir = Join-Path $modsDir "native"

    # Ensure directories exist
    if (-not (Test-Path $modsDir)) {
        New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
    }
    if (-not (Test-Path $rustModsDir)) {
        New-Item -ItemType Directory -Path $rustModsDir -Force | Out-Null
    }

    $deployed = 0

    # Copy C# MelonLoader plugin
    $csharpDll = Join-Path $ProjectRoot "csharp\DataCenterModLoader\bin\Release\net6.0\DataCenterModLoader.dll"
    if (Test-Path $csharpDll) {
        Copy-Item $csharpDll -Destination $modsDir -Force
        Write-Ok "  Mods/DataCenterModLoader.dll"
        $deployed++
    }
    else {
        Write-Warn "  DataCenterModLoader.dll not found (C# not built yet)."
    }

    # Copy Rust mod DLLs from target/release that match dc_* pattern
    # Excludes: dc_api.dll (shared lib), dc_api_macros.dll (proc-macro, compile-time only), dc_example_mod.dll (example)
    $releaseDir = Join-Path $ProjectRoot "target\release"
    if (Test-Path $releaseDir) {
        $extraMods = Get-ChildItem -Path $releaseDir -Filter "dc_*.dll" -ErrorAction SilentlyContinue |
            Where-Object { ($_.Name -ne "dc_example_mod.dll") -and ($_.Name -ne "dc_api.dll") -and ($_.Name -ne "dc_api_macros.dll") }

        foreach ($mod in $extraMods) {
            Copy-Item $mod.FullName -Destination $rustModsDir -Force
            Write-Ok ("  Mods/native/" + $mod.Name)
            $deployed++
        }
    }

    if ($deployed -eq 0) {
        Write-Warn "No files were deployed. Build the project first!"
    }
    else {
        Write-Ok ($deployed.ToString() + " file(s) deployed.")
    }
}

# ============================================================================
# Main
# ============================================================================

Write-Banner

# --- Find game ---
$resolvedGamePath = $GamePath

if ([string]::IsNullOrWhiteSpace($resolvedGamePath)) {
    $resolvedGamePath = Find-GamePath

    if (-not $resolvedGamePath) {
        Write-Host ""
        Write-Fail ("'" + $GAME_NAME + "' was not found in any Steam library!")
        Write-Host ""
        Write-Info "You can specify the path manually:"
        Write-Info '  .\install.ps1 -GamePath "D:\path\to\Data Center"'
        Write-Host ""
        exit 1
    }
}
else {
    # Validate manually provided path
    $exePath = Join-Path $resolvedGamePath $GAME_EXE
    if (-not (Test-Path $exePath)) {
        Write-Fail ("'" + $GAME_EXE + "' not found at: " + $resolvedGamePath)
        Write-Info "Make sure you specified the correct game directory."
        exit 1
    }
    Write-Ok ("Using provided game path: " + $resolvedGamePath)
}

Write-Host ""

# --- Install MelonLoader ---
Write-Step "Checking MelonLoader installation..."
$mlInstalled = Install-MelonLoader -TargetGamePath $resolvedGamePath

if (-not $mlInstalled) {
    Write-Host ""
    Write-Fail "MelonLoader is required but could not be installed."
    Write-Info "Install it manually from:"
    Write-Info ("  https://github.com/LavaGang/MelonLoader/releases/tag/" + $MELONLOADER_VERSION)
    Write-Host ""
    exit 1
}

Write-Host ""

# --- Build ---
Write-Step "Building projects..."
$buildOk = Build-Projects -TargetGamePath $resolvedGamePath

Write-Host ""

# --- Deploy ---
Deploy-ModFiles -TargetGamePath $resolvedGamePath

# --- Summary ---
Write-Host ""
Write-Host "  ============================================" -ForegroundColor Cyan
Write-Host "    Installation Summary"                       -ForegroundColor Cyan
Write-Host "  ============================================" -ForegroundColor Cyan
Write-Host ""

Write-Info ("Game path:       " + $resolvedGamePath)

$mlCheckPath = Join-Path $resolvedGamePath "MelonLoader"
if (Test-Path $mlCheckPath) {
    Write-Info "MelonLoader:     Installed"
}
else {
    Write-Info "MelonLoader:     Not found"
}

$csharpCheckPath = Join-Path $resolvedGamePath "Mods\DataCenterModLoader.dll"
if (Test-Path $csharpCheckPath) {
    Write-Info "C# Plugin:       Deployed"
}
else {
    Write-Info "C# Plugin:       Not deployed"
}

$rustModsCheckPath = Join-Path (Join-Path $resolvedGamePath "Mods") "native"
if (Test-Path $rustModsCheckPath) {
    $rustDlls = Get-ChildItem -Path $rustModsCheckPath -Filter "*.dll" -ErrorAction SilentlyContinue
    $rustDllCount = 0
    if ($rustDlls) {
        $rustDllCount = @($rustDlls).Count
    }
    Write-Info ("Rust mods:       " + $rustDllCount.ToString() + " DLL(s)")
}
else {
    Write-Info "Rust mods:       None"
}

Write-Host ""

# Check if first launch is needed
$il2cppAssemblies = Join-Path $resolvedGamePath "MelonLoader\Il2CppAssemblies"
if (-not (Test-Path $il2cppAssemblies)) {
    Write-Host "  ============================================" -ForegroundColor Yellow
    Write-Host "    NEXT STEP"                                  -ForegroundColor Yellow
    Write-Host "  ============================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Warn "This is a FRESH MelonLoader install."
    Write-Warn "You need to:"
    Write-Warn "  1. Launch Data Center through Steam"
    Write-Warn "  2. Wait for MelonLoader to generate Il2CppAssemblies"
    Write-Warn "     (the first launch will take 1-3 minutes longer)"
    Write-Warn "  3. Close the game"
    Write-Warn "  4. Run this script AGAIN to build and deploy the C# plugin"
    Write-Host ""
}
else {
    Write-Host "  ============================================" -ForegroundColor Green
    Write-Host "    READY TO GO!"                               -ForegroundColor Green
    Write-Host "  ============================================" -ForegroundColor Green
    Write-Host ""
    Write-Ok "Launch Data Center through Steam to test the modloader!"
    Write-Info "Check the MelonLoader console window for mod output."
    Write-Host ""
}
