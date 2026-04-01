// C# Script (.csx) — run with: dotnet script tools/dump_members.csx
// Or with: dotnet-script tools/dump_members.csx
//
// Dumps fields, properties, and methods for game types from the
// MelonLoader Il2CppAssemblies so we can write correct Harmony patches.
//
// Usage:
//   dotnet script tools/dump_members.csx
//   dotnet script tools/dump_members.csx -- Server Player CustomerBase

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// ── Configuration ──────────────────────────────────────────────────────────

string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Data Center";
string il2cppDir = Path.Combine(gameDir, "MelonLoader", "Il2CppAssemblies");
string net6Dir   = Path.Combine(gameDir, "MelonLoader", "net6");

string[] defaultTypes = new[]
{
    "Server", "CustomerBase", "Player", "PlayerManager",
    "TimeController", "MainGameManager", "ComputerShop",
    "HRSystem", "SaveSystem", "NetworkMap", "Rack",
    "BalanceSheet", "BuildingManager", "ContractBase",
    "Customer", "ContractManager"
};

// Allow overriding via command-line args
string[] requestedTypes = (Args != null && Args.Count > 0)
    ? Args.ToArray()
    : defaultTypes;

// ── Assembly loading ───────────────────────────────────────────────────────

Console.WriteLine("Loading assemblies...");

var resolver = new MetadataLoadContext(
    new PathAssemblyResolver(
        Directory.GetFiles(il2cppDir, "*.dll")
            .Concat(Directory.GetFiles(net6Dir, "*.dll"))
            .Concat(new[]
            {
                typeof(object).Assembly.Location,                               // System.Private.CoreLib
                Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "System.Runtime.dll")
            })
            .Distinct()
    )
);

Assembly asm;
try
{
    asm = resolver.LoadFromAssemblyPath(Path.Combine(il2cppDir, "Assembly-CSharp.dll"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load Assembly-CSharp.dll: {ex.Message}");
    return;
}

Type[] allTypes;
try
{
    allTypes = asm.GetTypes();
}
catch (ReflectionTypeLoadException ex)
{
    allTypes = ex.Types.Where(t => t != null).ToArray();
}

Console.WriteLine($"Loaded {allTypes.Length} types from Assembly-CSharp.dll");

// ── Helpers ────────────────────────────────────────────────────────────────

string FormatType(Type t)
{
    if (t == null) return "void";
    string name = t.Name;
    if (t.IsGenericType)
    {
        string baseName = name.Contains('`') ? name.Substring(0, name.IndexOf('`')) : name;
        string args = string.Join(", ", t.GetGenericArguments().Select(FormatType));
        return $"{baseName}<{args}>";
    }
    if (t.IsArray) return FormatType(t.GetElementType()) + "[]";
    if (t.IsByRef) return "ref " + FormatType(t.GetElementType());
    if (t.IsPointer) return FormatType(t.GetElementType()) + "*";
    return name switch
    {
        "Void"    => "void",
        "Boolean" => "bool",
        "Int32"   => "int",
        "Int64"   => "long",
        "UInt32"  => "uint",
        "UInt64"  => "ulong",
        "Single"  => "float",
        "Double"  => "double",
        "String"  => "string",
        "Byte"    => "byte",
        "SByte"   => "sbyte",
        "Int16"   => "short",
        "UInt16"  => "ushort",
        "Char"    => "char",
        "Object"  => "object",
        _         => name
    };
}

string AccessModifier(MethodBase m)
{
    if (m.IsPublic)   return "public";
    if (m.IsFamily)   return "protected";
    if (m.IsPrivate)  return "private";
    if (m.IsAssembly) return "internal";
    return "internal";
}

string AccessModifier(FieldInfo f)
{
    if (f.IsPublic)   return "public";
    if (f.IsFamily)   return "protected";
    if (f.IsPrivate)  return "private";
    if (f.IsAssembly) return "internal";
    return "internal";
}

// ── Dump logic ─────────────────────────────────────────────────────────────

var sb = new StringBuilder();
sb.AppendLine("==========================================================");
sb.AppendLine("  Data Center — Il2Cpp Type Dump");
sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
sb.AppendLine("==========================================================");

foreach (string typeName in requestedTypes)
{
    var matches = allTypes.Where(t =>
        t.Name == typeName ||
        t.FullName == typeName ||
        t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)
    ).ToList();

    if (matches.Count == 0)
    {
        // Fuzzy search
        var fuzzy = allTypes
            .Where(t => t.Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(t => t.FullName)
            .Take(15)
            .ToList();

        sb.AppendLine();
        sb.AppendLine($"TYPE NOT FOUND: {typeName}");
        if (fuzzy.Count > 0)
        {
            sb.AppendLine("  Fuzzy matches:");
            foreach (var f in fuzzy)
                sb.AppendLine($"    - {f.FullName}");
        }
        sb.AppendLine();
        continue;
    }

    foreach (var type in matches)
    {
        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════");
        sb.AppendLine($"  TYPE: {type.FullName}");
        if (type.BaseType != null)
            sb.AppendLine($"  Base: {type.BaseType.FullName}");
        sb.AppendLine($"  Public: {type.IsPublic}  Abstract: {type.IsAbstract}  Sealed: {type.IsSealed}");

        // Interfaces
        try
        {
            var interfaces = type.GetInterfaces();
            if (interfaces.Length > 0)
            {
                sb.AppendLine($"  Implements: {string.Join(", ", interfaces.Select(i => FormatType(i)))}");
            }
        }
        catch { }

        sb.AppendLine("══════════════════════════════════════════════════════════");

        // ── Fields ──
        sb.AppendLine();
        sb.AppendLine("  ── FIELDS ──");

        var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

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
                string access = AccessModifier(field);
                string stat = field.IsStatic ? " static" : "";
                string ro = field.IsInitOnly ? " readonly" : "";
                string lit = field.IsLiteral ? " const" : "";
                string ftype = FormatType(field.FieldType);
                sb.AppendLine($"    {access}{stat}{ro}{lit} {ftype} {field.Name}");
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
        }

        // ── Methods ──
        sb.AppendLine();
        sb.AppendLine("  ── METHODS ──");

        MethodInfo[] methods;
        try
        {
            methods = type.GetMethods(flags)
                .Where(m => !m.IsSpecialName)  // skip property getters/setters, event add/remove
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
                string handlerType = FormatType(evt.EventHandlerType);
                sb.AppendLine($"    event {handlerType} {evt.Name}");
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
            }
        }

        sb.AppendLine();
    }
}

// Write output
string scriptDir = Path.GetDirectoryName(
    Environment.GetCommandLineArgs().Length > 0
        ? Path.GetFullPath("tools")
        : ".");
string outputPath = Path.Combine("tools", "type_dump_output.txt");

string result = sb.ToString();
File.WriteAllText(outputPath, result, Encoding.UTF8);

Console.WriteLine();
Console.WriteLine(result);
Console.WriteLine();
Console.WriteLine($"Output saved to: {Path.GetFullPath(outputPath)}");

resolver.Dispose();
