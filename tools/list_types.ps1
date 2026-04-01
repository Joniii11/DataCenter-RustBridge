$ErrorActionPreference = "Continue"

$il2cppDir = "C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies"
$asmPath = Join-Path $il2cppDir "Assembly-CSharp.dll"

if (-not (Test-Path $asmPath)) {
    Write-Host "Assembly-CSharp.dll not found at: $asmPath"
    exit 1
}

# Preload dependencies
$net6Dir = "C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader\net6"
foreach ($dir in @($net6Dir, $il2cppDir)) {
    if (Test-Path $dir) {
        foreach ($dll in (Get-ChildItem -Path $dir -Filter "*.dll" -ErrorAction SilentlyContinue)) {
            try { [System.Reflection.Assembly]::LoadFile($dll.FullName) | Out-Null } catch { }
        }
    }
}

$asm = [System.Reflection.Assembly]::LoadFile($asmPath)
$types = $null
try {
    $types = $asm.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types
}

$outputFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) "all_types.txt"
$lines = @()

foreach ($t in ($types | Where-Object { $_ -ne $null } | Sort-Object FullName)) {
    $lines += $t.FullName
}

Set-Content -Path $outputFile -Value ($lines -join "`n") -Encoding UTF8
Write-Host "Wrote $($lines.Count) types to $outputFile"
