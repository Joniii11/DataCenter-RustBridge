param(
    [string[]]$Types = @('Server', 'ServerSaveData', 'RackPosition', 'Rack', 'SaveData'),
    [string]$DumpDir = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) "dump")
)

$ErrorActionPreference = "Continue"

if (-not (Test-Path $DumpDir)) {
    Write-Host "ERROR: Dump directory not found at: $DumpDir" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "  ============================================" -ForegroundColor Cyan
Write-Host "    Dump Type Inspector" -ForegroundColor Cyan
Write-Host "  ============================================" -ForegroundColor Cyan
Write-Host "  Dump dir: $DumpDir" -ForegroundColor Gray
Write-Host ""

$assemblies = @{}
foreach ($dll in (Get-ChildItem -Path $DumpDir -Filter '*.dll' -ErrorAction SilentlyContinue)) {
    try {
        $asm = [System.Reflection.Assembly]::LoadFile($dll.FullName)
        $assemblies[$dll.Name] = $asm
    } catch { }
}
Write-Host "  Loaded $($assemblies.Count) assemblies." -ForegroundColor Gray
Write-Host ""

function Format-TypeName {
    param([Type]$Type)
    if ($null -eq $Type) { return "void" }
    $name = $Type.Name
    if ($Type.IsGenericType) {
        $baseName = $name -replace '`\d+$', ''
        $genericArgs = ($Type.GetGenericArguments() | ForEach-Object { Format-TypeName $_ }) -join ', '
        return "$baseName<$genericArgs>"
    }
    switch ($name) {
        'Void' { return 'void' }
        'Boolean' { return 'bool' }
        'Int32' { return 'int' }
        'Int64' { return 'long' }
        'Single' { return 'float' }
        'Double' { return 'double' }
        'String' { return 'string' }
        'UInt32' { return 'uint' }
        'UInt64' { return 'ulong' }
        'Byte' { return 'byte' }
        'Char' { return 'char' }
        'Int16' { return 'short' }
        'UInt16' { return 'ushort' }
        default { return $name }
    }
}

function Dump-Type {
    param([string]$TypeName)

    $found = $false
    foreach ($asmEntry in $assemblies.GetEnumerator()) {
        $allTypes = $null
        try { $allTypes = $asmEntry.Value.GetTypes() }
        catch [System.Reflection.ReflectionTypeLoadException] {
            $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null }
        }
        catch { continue }
        if ($null -eq $allTypes) { continue }

        $matched = $allTypes | Where-Object {
            $_ -ne $null -and ($_.Name -eq $TypeName -or $_.FullName -eq $TypeName)
        }

        foreach ($type in $matched) {
            $found = $true
            Write-Host ""
            Write-Host "  ================================================================" -ForegroundColor Yellow
            Write-Host "  TYPE: $($type.FullName)" -ForegroundColor Yellow
            Write-Host "  Assembly: $($asmEntry.Key)" -ForegroundColor Gray
            if ($type.BaseType) {
                Write-Host "  BaseType: $($type.BaseType.FullName)" -ForegroundColor Gray
            }
            $interfaces = $type.GetInterfaces()
            if ($interfaces.Count -gt 0) {
                Write-Host "  Implements: $(($interfaces | ForEach-Object { $_.Name }) -join ', ')" -ForegroundColor Gray
            }
            Write-Host "  IsPublic=$($type.IsPublic) IsSealed=$($type.IsSealed) IsAbstract=$($type.IsAbstract) IsEnum=$($type.IsEnum) IsValueType=$($type.IsValueType)" -ForegroundColor Gray
            Write-Host "  ================================================================" -ForegroundColor Yellow

            if ($type.IsEnum) {
                Write-Host ""
                Write-Host "  --- ENUM VALUES ---" -ForegroundColor Cyan
                $names = [System.Enum]::GetNames($type)
                $values = [System.Enum]::GetValues($type)
                for ($i = 0; $i -lt $names.Count; $i++) {
                    $v = [Convert]::ToInt64($values.GetValue($i))
                    Write-Host "    $($names[$i]) = $v"
                }
            }

            # Fields
            $flags = [System.Reflection.BindingFlags]::Public -bor
                     [System.Reflection.BindingFlags]::NonPublic -bor
                     [System.Reflection.BindingFlags]::Instance -bor
                     [System.Reflection.BindingFlags]::Static -bor
                     [System.Reflection.BindingFlags]::DeclaredOnly

            $fields = $type.GetFields($flags)
            Write-Host ""
            Write-Host "  --- FIELDS ($($fields.Count)) ---" -ForegroundColor Cyan
            foreach ($f in ($fields | Sort-Object { $_.IsStatic }, Name)) {
                $acc = if ($f.IsPublic) { "public" } elseif ($f.IsFamily) { "protected" } elseif ($f.IsPrivate) { "private" } else { "internal" }
                $stat = if ($f.IsStatic) { " static" } else { "" }
                $ro = if ($f.IsInitOnly) { " readonly" } else { "" }
                $ft = Format-TypeName $f.FieldType
                Write-Host "    $acc$stat$ro $ft $($f.Name)"
            }

            # Properties
            $props = $type.GetProperties($flags)
            Write-Host ""
            Write-Host "  --- PROPERTIES ($($props.Count)) ---" -ForegroundColor Cyan
            foreach ($p in ($props | Sort-Object Name)) {
                $pt = Format-TypeName $p.PropertyType
                $g = if ($p.GetGetMethod($true)) { "get; " } else { "" }
                $s = if ($p.GetSetMethod($true)) { "set; " } else { "" }
                $gm = $p.GetGetMethod($true)
                if ($null -eq $gm) { $gm = $p.GetSetMethod($true) }
                $stat = ""
                if ($null -ne $gm -and $gm.IsStatic) { $stat = " static" }
                Write-Host "    $pt$stat $($p.Name) { $g$s}"
            }

            # Methods (non-special)
            $methods = $type.GetMethods($flags) | Where-Object { -not $_.IsSpecialName }
            Write-Host ""
            Write-Host "  --- METHODS ($($methods.Count)) ---" -ForegroundColor Cyan
            foreach ($m in ($methods | Sort-Object { $_.IsStatic }, Name)) {
                $acc = if ($m.IsPublic) { "public" } elseif ($m.IsFamily) { "protected" } elseif ($m.IsPrivate) { "private" } else { "internal" }
                $stat = if ($m.IsStatic) { " static" } else { "" }
                $virt = if ($m.IsVirtual -and -not $m.IsFinal) { " virtual" } else { "" }
                $rt = Format-TypeName $m.ReturnType
                $params = ($m.GetParameters() | ForEach-Object {
                    $pn = Format-TypeName $_.ParameterType
                    "$pn $($_.Name)"
                }) -join ', '
                Write-Host "    $acc$stat$virt $rt $($m.Name)($params)"
            }

            # Nested types
            $nested = $type.GetNestedTypes($flags)
            if ($nested.Count -gt 0) {
                Write-Host ""
                Write-Host "  --- NESTED TYPES ($($nested.Count)) ---" -ForegroundColor Cyan
                foreach ($nt in $nested) {
                    Write-Host "    $($nt.FullName)"
                }
            }

            Write-Host ""
        }
    }

    if (-not $found) {
        Write-Host ""
        Write-Host "  TYPE NOT FOUND: $TypeName" -ForegroundColor Red

        # Fuzzy search
        $fuzzy = @()
        foreach ($asmEntry in $assemblies.GetEnumerator()) {
            try {
                $allTypes = $asmEntry.Value.GetTypes()
            } catch [System.Reflection.ReflectionTypeLoadException] {
                $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null }
            } catch { continue }
            if ($null -eq $allTypes) { continue }
            $fuzzy += $allTypes | Where-Object { $_ -ne $null -and $_.Name -like "*$TypeName*" }
        }
        if ($fuzzy.Count -gt 0) {
            Write-Host "  Similar types:" -ForegroundColor Yellow
            foreach ($ft in ($fuzzy | Sort-Object FullName | Select-Object -First 25)) {
                Write-Host "    - $($ft.FullName) [$($ft.Assembly.GetName().Name)]"
            }
        }
        Write-Host ""
    }
}

foreach ($t in $Types) {
    Write-Host "  Inspecting: $t ..." -ForegroundColor White
    Dump-Type -TypeName $t
}

Write-Host "  Done." -ForegroundColor Cyan
