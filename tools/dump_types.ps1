# Data Center Modloader — Type Dumper
# Runs Cpp2IL to dump the game's IL2CPP types.

param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Data Center",
    [string]$OutputPath = (Join-Path $PSScriptRoot "dump")
)

$ErrorActionPreference = "Stop"

Write-Host "Data Center — IL2CPP Type Dumper" -ForegroundColor Cyan
Write-Host ""

# Look for Cpp2IL
$cpp2il = Get-Command "Cpp2IL.exe" -ErrorAction SilentlyContinue
if (-not $cpp2il) {
    $cpp2il = Get-ChildItem -Path $PSScriptRoot -Filter "Cpp2IL.exe" -Recurse | Select-Object -First 1
}

if (-not $cpp2il) {
    Write-Host "ERROR: Cpp2IL.exe not found!" -ForegroundColor Red
    Write-Host "Download from: https://github.com/SamboyCoding/Cpp2IL/releases" -ForegroundColor Yellow
    Write-Host "Place it in: $PSScriptRoot" -ForegroundColor Yellow
    exit 1
}

$cpp2ilPath = if ($cpp2il -is [System.Management.Automation.ApplicationInfo]) { $cpp2il.Source } else { $cpp2il.FullName }
Write-Host "Using Cpp2IL: $cpp2ilPath" -ForegroundColor Green

# Run Cpp2IL
Write-Host "Dumping types to: $OutputPath" -ForegroundColor Yellow
& $cpp2ilPath --game-path $GamePath --output-as dummydll --output-to $OutputPath

Write-Host ""
Write-Host "Done! Open the DLLs in dnSpy or ILSpy to browse game types." -ForegroundColor Green
