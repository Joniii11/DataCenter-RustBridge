using System.Reflection;
using System.Text;

// ── Configuration ──────────────────────────────────────────────────────────

string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Data Center";
string il2cppDir = Path.Combine(gameDir, "MelonLoader", "Il2CppAssemblies");
string net6Dir = Path.Combine(gameDir, "MelonLoader", "net6");

string[] defaultTypes = new[]
{
    "Server", "CustomerBase", "Player", "PlayerManager",
    "TimeController", "MainGameManager", "ComputerShop",
    "HRSystem", "SaveSystem", "NetworkMap", "Rack",
    "BalanceSheet", "BuildingManager", "ContractBase",
    "Customer", "ContractManager",
    "TechnicianManager", "Technician", "TechnicianState",
    "NetworkSwitch", "AssetManagement"
};

string[] requestedTypes = args.Length > 0 ? args : defaultTypes;

if (!Directory.Exists(il2cppDir))
{
    Console.Error.WriteLine($"Il2CppAssemblies not found at: {il2cppDir}");
    Console.Error.WriteLine("Make sure MelonLoader has been run at least once.");
    return 1;
}

// ── Assembly loading via MetadataLoadContext ────────────────────────────────

Console.WriteLine("Loading assemblies...");

var assemblyPaths = new List<string>();

if (Directory.Exists(il2cppDir))
    assemblyPaths.AddRange(Directory.GetFiles(il2cppDir, "*.dll"));
if (Directory.Exists(net6Dir))
    assemblyPaths.AddRange(Directory.GetFiles(net6Dir, "*.dll"));

// Add runtime assemblies for core type resolution
string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
assemblyPaths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

// Deduplicate by filename (prefer Il2Cpp/net6 over runtime)
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var uniquePaths = new List<string>();
foreach (var p in assemblyPaths)
{
    string name = Path.GetFileName(p);
    if (seen.Add(name))
        uniquePaths.Add(p);
}

var resolver = new PathAssemblyResolver(uniquePaths);
using var mlc = new MetadataLoadContext(resolver, coreAssemblyName: "System.Private.CoreLib");

string asmCSharpPath = Path.Combine(il2cppDir, "Assembly-CSharp.dll");
if (!File.Exists(asmCSharpPath))
{
    Console.Error.WriteLine($"Assembly-CSharp.dll not found at: {asmCSharpPath}");
    return 1;
}

Assembly asm;
try
{
    asm = mlc.LoadFromAssemblyPath(asmCSharpPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load Assembly-CSharp.dll: {ex.Message}");
    return 1;
}

Type[] allTypes;
try
{
    allTypes = asm.GetTypes();
}
catch (ReflectionTypeLoadException ex)
{
    allTypes = ex.Types.Where(t => t != null).ToArray()!;
    Console.WriteLine($"Warning: Some types failed to load ({ex.LoaderExceptions.Length} errors)");
}

Console.WriteLine($"Loaded {allTypes.Length} types from Assembly-CSharp.dll");

// ── Helpers ────────────────────────────────────────────────────────────────

string FormatType(Type? t)
{
    if (t == null) return "void";
    if (t.IsGenericType)
    {
        string baseName = t.Name.Contains('`') ? t.Name[..t.Name.IndexOf('`')] : t.Name;
        string args = string.Join(", ", t.GetGenericArguments().Select(FormatType));
        return $"{baseName}<{args}>";
    }
    if (t.IsArray) return FormatType(t.GetElementType()) + "[]";
    if (t.IsByRef) return "ref " + FormatType(t.GetElementType());
    if (t.IsPointer) return FormatType(t.GetElementType()) + "*";
    return t.Name switch
    {
        "Void" => "void",
        "Boolean" => "bool",
        "Int32" => "int",
        "Int64" => "long",
        "UInt32" => "uint",
        "UInt64" => "ulong",
        "Single" => "float",
        "Double" => "double",
        "String" => "string",
        "Byte" => "byte",
        "SByte" => "sbyte",
        "Int16" => "short",
        "UInt16" => "ushort",
        "Char" => "char",
        "Object" => "object",
        "IntPtr" => "IntPtr",
        _ => t.Name
    };
}

string AccessModifier(MethodBase m)
{
    if (m.IsPublic) return "public";
    if (m.IsFamily) return "protected";
    if (m.IsPrivate) return "private";
    if (m.IsAssembly) return "internal";
    if (m.IsFamilyOrAssembly) return "protected internal";
    return "internal";
}

string FieldAccessModifier(FieldInfo f)
{
    if (f.IsPublic) return "public";
    if (f.IsFamily) return "protected";
    if (f.IsPrivate) return "private";
    if (f.IsAssembly) return "internal";
    if (f.IsFamilyOrAssembly) return "protected internal";
    return "internal";
}

// ── Dump logic ─────────────────────────────────────────────────────────────

var sb = new StringBuilder();
sb.AppendLine("==========================================================");
sb.AppendLine("  Data Center — Il2Cpp Type Dump");
sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
sb.AppendLine($"  Total types in Assembly-CSharp: {allTypes.Length}");
sb.AppendLine("==========================================================");

foreach (string typeName in requestedTypes)
{
    var matches = allTypes.Where(t =>
        t.Name.Equals(typeName, StringComparison.Ordinal) ||
        t.FullName?.Equals(typeName, StringComparison.Ordinal) == true ||
        t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)
    ).ToList();

    if (matches.Count == 0)
    {
        // Fuzzy search
        var fuzzy = allTypes
            .Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase) ||
                        (t.FullName?.Contains(typeName, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(t => t.FullName)
            .Take(20)
            .ToList();

        sb.AppendLine();
        sb.AppendLine($"╔══ TYPE NOT FOUND: {typeName} ══╗");
        if (fuzzy.Count > 0)
        {
            sb.AppendLine("  Fuzzy matches:");
            foreach (var f in fuzzy)
                sb.AppendLine($"    - {f.FullName}");
        }
        else
        {
            sb.AppendLine("  No fuzzy matches found either.");
        }
        sb.AppendLine();
        continue;
    }

    foreach (var type in matches)
    {
        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════════════");
        sb.AppendLine($"  TYPE: {type.FullName}");
        try
        {
            if (type.BaseType != null)
                sb.AppendLine($"  Base: {type.BaseType.FullName}");
        }
        catch { sb.AppendLine("  Base: (could not resolve)"); }

        sb.AppendLine($"  Public: {type.IsPublic}  Abstract: {type.IsAbstract}  Sealed: {type.IsSealed}");

        // Interfaces
        try
        {
            var interfaces = type.GetInterfaces();
            if (interfaces.Length > 0)
                sb.AppendLine($"  Implements: {string.Join(", ", interfaces.Select(i => FormatType(i)))}");
        }
        catch { }

        sb.AppendLine("══════════════════════════════════════════════════════════════════");

        var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // ── Fields ──
        sb.AppendLine();
        sb.AppendLine("  ── FIELDS ──");

        FieldInfo[] fields;
        try { fields = type.GetFields(flags); }
        catch { fields = Array.Empty<FieldInfo>(); }

        if (fields.Length == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var field in fields.OrderBy(f => f.IsStatic ? 0 : 1).ThenBy(f => f.Name))
            {
                try
                {
                    string access = FieldAccessModifier(field);
                    string stat = field.IsStatic ? " static" : "";
                    string ro = field.IsInitOnly ? " readonly" : "";
                    string lit = field.IsLiteral ? " const" : "";
                    string ftype = FormatType(field.FieldType);
                    sb.AppendLine($"    {access}{stat}{ro}{lit} {ftype} {field.Name}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"    ??? {field.Name} (error: {ex.Message})");
                }
            }
        }

        // ── Properties ──
        sb.AppendLine();
        sb.AppendLine("  ── PROPERTIES ──");

        PropertyInfo[] props;
        try { props = type.GetProperties(flags); }
        catch { props = Array.Empty<PropertyInfo>(); }

        if (props.Length == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var prop in props.OrderBy(p => p.Name))
            {
                try
                {
                    string ptype = FormatType(prop.PropertyType);
                    string get = "", set = "";
                    try { get = prop.GetGetMethod(true) != null ? "get; " : ""; } catch { }
                    try { set = prop.GetSetMethod(true) != null ? "set; " : ""; } catch { }
                    string stat = "";
                    try
                    {
                        var getter = prop.GetGetMethod(true) ?? prop.GetSetMethod(true);
                        if (getter != null && getter.IsStatic) stat = " static";
                    }
                    catch { }
                    sb.AppendLine($"    {ptype}{stat} {prop.Name} {{ {get}{set}}}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"    ??? {prop.Name} (error: {ex.Message})");
                }
            }
        }

        // ── Methods ──
        sb.AppendLine();
        sb.AppendLine("  ── METHODS ──");

        MethodInfo[] methods;
        try
        {
            methods = type.GetMethods(flags)
                .Where(m => !m.IsSpecialName)
                .ToArray();
        }
        catch { methods = Array.Empty<MethodInfo>(); }

        if (methods.Length == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var method in methods.OrderBy(m => m.IsStatic ? 0 : 1).ThenBy(m => m.Name))
            {
                try
                {
                    string access = AccessModifier(method);
                    string stat = method.IsStatic ? " static" : "";
                    string virt = "";
                    if (method.IsVirtual && !method.IsFinal)
                    {
                        try
                        {
                            if (method.GetBaseDefinition().DeclaringType != type)
                                virt = " override";
                            else
                                virt = " virtual";
                        }
                        catch { virt = " virtual"; }
                    }
                    string retType = FormatType(method.ReturnType);

                    string paramStr;
                    try
                    {
                        var pars = method.GetParameters();
                        paramStr = string.Join(", ", pars.Select(p =>
                        {
                            string pt = FormatType(p.ParameterType);
                            string def = "";
                            try
                            {
                                if (p.HasDefaultValue)
                                {
                                    if (p.DefaultValue == null) def = " = null";
                                    else if (p.DefaultValue is bool b) def = $" = {(b ? "true" : "false")}";
                                    else if (p.DefaultValue is string s) def = $" = \"{s}\"";
                                    else def = $" = {p.DefaultValue}";
                                }
                            }
                            catch { }
                            return $"{pt} {p.Name}{def}";
                        }));
                    }
                    catch
                    {
                        paramStr = "???";
                    }

                    sb.AppendLine($"    {access}{stat}{virt} {retType} {method.Name}({paramStr})");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"    ??? {method.Name} (error: {ex.Message})");
                }
            }
        }

        // ── Constructors ──
        ConstructorInfo[] ctors;
        try { ctors = type.GetConstructors(flags); }
        catch { ctors = Array.Empty<ConstructorInfo>(); }

        if (ctors.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── CONSTRUCTORS ──");
            foreach (var ctor in ctors)
            {
                try
                {
                    string access = AccessModifier(ctor);
                    string paramStr;
                    try
                    {
                        var pars = ctor.GetParameters();
                        paramStr = string.Join(", ", pars.Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
                    }
                    catch { paramStr = "???"; }
                    sb.AppendLine($"    {access} .ctor({paramStr})");
                }
                catch { }
            }
        }

        // ── Events ──
        EventInfo[] events;
        try { events = type.GetEvents(flags); }
        catch { events = Array.Empty<EventInfo>(); }

        if (events.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── EVENTS ──");
            foreach (var evt in events.OrderBy(e => e.Name))
            {
                try
                {
                    string handlerType = FormatType(evt.EventHandlerType);
                    sb.AppendLine($"    event {handlerType} {evt.Name}");
                }
                catch { }
            }
        }

        // ── Nested types ──
        Type[] nested;
        try { nested = type.GetNestedTypes(flags); }
        catch { nested = Array.Empty<Type>(); }

        if (nested.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ── NESTED TYPES ──");
            foreach (var nt in nested.OrderBy(n => n.Name))
            {
                string kind = nt.IsEnum ? "enum" : nt.IsValueType ? "struct" : nt.IsInterface ? "interface" : "class";
                sb.AppendLine($"    {kind} {nt.Name}");

                // If it's an enum, show its values
                if (nt.IsEnum)
                {
                    try
                    {
                        var enumFields = nt.GetFields(BindingFlags.Public | BindingFlags.Static);
                        foreach (var ef in enumFields)
                        {
                            try
                            {
                                var val = ef.GetRawConstantValue();
                                sb.AppendLine($"      {ef.Name} = {val}");
                            }
                            catch
                            {
                                sb.AppendLine($"      {ef.Name}");
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        sb.AppendLine();
    }
}

// ── Write output ───────────────────────────────────────────────────────────

string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "type_dump_output.txt");
// Also try to write next to the script
string altOutputPath = Path.Combine("tools", "type_dump_output.txt");

string result = sb.ToString();

try
{
    File.WriteAllText(altOutputPath, result, Encoding.UTF8);
    Console.WriteLine($"\nOutput saved to: {Path.GetFullPath(altOutputPath)}");
}
catch
{
    try
    {
        File.WriteAllText("type_dump_output.txt", result, Encoding.UTF8);
        Console.WriteLine($"\nOutput saved to: {Path.GetFullPath("type_dump_output.txt")}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to write output file: {ex.Message}");
    }
}

// Print to console
Console.WriteLine();
Console.WriteLine(result);

return 0;
