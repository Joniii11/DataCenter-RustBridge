# Inspect Unity.InputSystem.dll for available types related to Keyboard/Key
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Data Center"
)

$dllPath = Join-Path $GameDir "MelonLoader\Il2CppAssemblies\Unity.InputSystem.dll"

if (-not (Test-Path $dllPath)) {
    Write-Error "Could not find: $dllPath"
    exit 1
}

Write-Host "Loading: $dllPath" -ForegroundColor Cyan
$asm = [System.Reflection.Assembly]::LoadFrom($dllPath)

$types = $null
try {
    $types = $asm.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    Write-Host "ReflectionTypeLoadException - using partial type list" -ForegroundColor Yellow
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
}

Write-Host ""
Write-Host "=== Types containing 'Keyboard' ===" -ForegroundColor Green
$types | Where-Object { $_.Name -like '*Keyboard*' } | ForEach-Object {
    Write-Host "  $($_.FullName)"
}

Write-Host ""
Write-Host "=== Types containing 'Key' (exact class name) ===" -ForegroundColor Green
$types | Where-Object { $_.Name -eq 'Key' } | ForEach-Object {
    Write-Host "  $($_.FullName)"
    if ($_.IsEnum) {
        Write-Host "    (Enum values):" -ForegroundColor DarkGray
        [System.Enum]::GetNames($_) | ForEach-Object { Write-Host "      $_" }
    }
}

Write-Host ""
Write-Host "=== Types containing 'ButtonControl' ===" -ForegroundColor Green
$types | Where-Object { $_.Name -like '*ButtonControl*' } | ForEach-Object {
    Write-Host "  $($_.FullName)"
}

Write-Host ""
Write-Host "=== Types containing 'KeyControl' ===" -ForegroundColor Green
$types | Where-Object { $_.Name -like '*KeyControl*' } | ForEach-Object {
    Write-Host "  $($_.FullName)"
}

Write-Host ""
Write-Host "=== All top-level InputSystem types ===" -ForegroundColor Green
$types | Where-Object { $_.Namespace -like '*InputSystem*' -and -not $_.IsNested } | Sort-Object FullName | ForEach-Object {
    $suffix = ""
    if ($_.IsEnum) { $suffix = " [Enum]" }
    elseif ($_.IsInterface) { $suffix = " [Interface]" }
    elseif ($_.IsAbstract) { $suffix = " [Abstract]" }
    Write-Host "  $($_.FullName)$suffix"
}

Write-Host ""
Write-Host "=== Keyboard class members (if found) ===" -ForegroundColor Green
$kbType = $types | Where-Object { $_.Name -eq 'Keyboard' } | Select-Object -First 1
if ($kbType) {
    Write-Host "  Full name: $($kbType.FullName)" -ForegroundColor Cyan

    Write-Host "  --- Static Properties ---" -ForegroundColor DarkCyan
    $kbType.GetProperties([System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public) | ForEach-Object {
        Write-Host "    $($_.PropertyType.Name) $($_.Name) { get; }"
    }

    Write-Host "  --- Instance Properties ---" -ForegroundColor DarkCyan
    $kbType.GetProperties([System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public) | ForEach-Object {
        Write-Host "    $($_.PropertyType.Name) $($_.Name) { get; }"
    }

    Write-Host "  --- Public Methods ---" -ForegroundColor DarkCyan
    $kbType.GetMethods([System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public) |
        Where-Object { -not $_.IsSpecialName } |
        ForEach-Object {
            $params = ($_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
            Write-Host "    $($_.ReturnType.Name) $($_.Name)($params)"
        }

    Write-Host "  --- Indexer ---" -ForegroundColor DarkCyan
    $kbType.GetProperties([System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public) |
        Where-Object { $_.GetIndexParameters().Count -gt 0 } |
        ForEach-Object {
            $idxParams = ($_.GetIndexParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
            Write-Host "    $($_.PropertyType.Name) this[$idxParams] { get; }"
        }
} else {
    Write-Host "  Keyboard type not found!" -ForegroundColor Red
}
