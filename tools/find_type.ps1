param(
    [string]$SearchPattern = '*PlayerManager*'
)

$ErrorActionPreference = 'Continue'

$mlDir = 'C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader'
$il2cppDir = Join-Path $mlDir 'Il2CppAssemblies'
$net6Dir = Join-Path $mlDir 'net6'

Write-Host "Searching for types matching: $SearchPattern"
Write-Host ''

# Preload dependencies first
Write-Host 'Preloading dependencies...'
$preloaded = 0
foreach ($dir in @($net6Dir, $il2cppDir)) {
    if (-not (Test-Path $dir)) { continue }
    foreach ($dll in (Get-ChildItem -Path $dir -Filter '*.dll' -ErrorAction SilentlyContinue)) {
        try {
            [System.Reflection.Assembly]::LoadFile($dll.FullName) | Out-Null
            $preloaded++
        } catch { }
    }
}
Write-Host "Preloaded $preloaded assemblies."
Write-Host ''

# Now search specifically in Assembly-CSharp.dll
$asmPath = Join-Path $il2cppDir 'Assembly-CSharp.dll'
if (Test-Path $asmPath) {
    Write-Host "=== Assembly-CSharp.dll ==="
    try {
        $asm = [System.Reflection.Assembly]::LoadFile((Resolve-Path $asmPath).Path)
        $types = $null
        try {
            $types = $asm.GetTypes()
        } catch [System.Reflection.ReflectionTypeLoadException] {
            $types = $_.Exception.Types
        }

        if ($types) {
            $matched = $types | Where-Object { $_ -ne $null -and $_.FullName -like $SearchPattern }
            if ($matched) {
                foreach ($t in ($matched | Sort-Object FullName)) {
                    $ns = if ($t.Namespace) { $t.Namespace } else { '<global>' }
                    $base = ''
                    if ($t.BaseType) { $base = " : $($t.BaseType.FullName)" }
                    Write-Host "  Namespace: $ns"
                    Write-Host "  FullName:  $($t.FullName)$base"
                    Write-Host "  IsPublic:  $($t.IsPublic)"
                    Write-Host ''
                }
                Write-Host "Found $(@($matched).Count) match(es)."
            } else {
                Write-Host "  No types matching '$SearchPattern' found."
                Write-Host ''
                Write-Host '  Listing ALL top-level type names (first 50):'
                $allTypes = $types | Where-Object { $_ -ne $null -and $_.FullName -notmatch '\+' } | Sort-Object FullName | Select-Object -First 50
                foreach ($t in $allTypes) {
                    $ns = if ($t.Namespace) { "[$($t.Namespace)] " } else { '' }
                    Write-Host "    ${ns}$($t.Name)"
                }
                $totalCount = @($types | Where-Object { $_ -ne $null -and $_.FullName -notmatch '\+' }).Count
                Write-Host ''
                Write-Host "  (Showing 50 / $totalCount total top-level types)"
            }
        } else {
            Write-Host '  Could not load any types from assembly.'
        }
    } catch {
        Write-Host "  Error loading assembly: $($_.Exception.Message)"
    }
} else {
    Write-Host "Assembly-CSharp.dll not found at: $asmPath"
}

Write-Host ''
Write-Host 'Done.'
