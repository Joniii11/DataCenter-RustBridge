using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main(string[] args)
    {
        string asmDir = @"C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies";
        string melonNet6Dir = @"C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader\net6";
        string targetDll = Path.Combine(asmDir, "Assembly-CSharp.dll");

        if (!File.Exists(targetDll))
        {
            Console.Error.WriteLine($"File not found: {targetDll}");
            return;
        }

        // Collect all DLLs in the assemblies directory as resolver paths
        var runtimeAssemblies = Directory.GetFiles(asmDir, "*.dll").ToList();

        // Add MelonLoader net6 directory (contains Il2CppInterop.Runtime.dll etc.)
        if (Directory.Exists(melonNet6Dir))
        {
            foreach (var dll in Directory.GetFiles(melonNet6Dir, "*.dll"))
            {
                if (!runtimeAssemblies.Any(r => Path.GetFileName(r).Equals(Path.GetFileName(dll), StringComparison.OrdinalIgnoreCase)))
                {
                    runtimeAssemblies.Add(dll);
                }
            }
        }

        // Also add the core runtime assemblies so MetadataLoadContext can resolve System types
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            if (!runtimeAssemblies.Any(r => Path.GetFileName(r).Equals(Path.GetFileName(dll), StringComparison.OrdinalIgnoreCase)))
            {
                runtimeAssemblies.Add(dll);
            }
        }

        var resolver = new PathAssemblyResolver(runtimeAssemblies);
        using var mlc = new MetadataLoadContext(resolver);

        Assembly asm;
        try
        {
            asm = mlc.LoadFromAssemblyPath(targetDll);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load assembly: {ex.Message}");
            return;
        }

        Type[] allTypes;
        try
        {
            allTypes = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
            Console.Error.WriteLine($"Partial type load. {ex.LoaderExceptions.Length} loader exceptions (showing first 5):");
            foreach (var le in ex.LoaderExceptions.Take(5))
            {
                Console.Error.WriteLine($"  {le?.Message}");
            }
        }

        Console.WriteLine($"=== ALL TYPES IN Assembly-CSharp.dll ({allTypes.Length} total) ===");
        Console.WriteLine();

        var sorted = allTypes.OrderBy(t => { try { return t.FullName ?? t.Name; } catch { return t.Name; } }).ToList();
        foreach (var t in sorted)
        {
            string kind, baseName = "", vis = "";
            try
            {
                kind = t.IsEnum ? "enum" :
                       t.IsInterface ? "interface" :
                       t.IsValueType ? "struct" :
                       t.IsClass ? "class" : "type";
            }
            catch { kind = "type"; }

            try
            {
                if (t.BaseType != null && t.BaseType.Name != "Object" && t.BaseType.Name != "ValueType" && t.BaseType.Name != "Enum")
                    baseName = $" : {t.BaseType.Name}";
            }
            catch { }

            try
            {
                vis = t.IsPublic || t.IsNestedPublic ? "public" :
                      t.IsNestedPrivate ? "private" :
                      t.IsNestedFamily ? "protected" :
                      t.IsNotPublic ? "internal" : "";
            }
            catch { }

            try { Console.WriteLine($"  {vis} {kind} {t.FullName ?? t.Name}{baseName}"); }
            catch { try { Console.WriteLine($"  {vis} {kind} {t.Name}{baseName}"); } catch { } }
        }

        Console.WriteLine();
        Console.WriteLine("=== FILTERED: Menu, Button, Setting, Local, Panel, UI, Config, Save, Load, Option, Preference, Language, Input, Keybind, Resolution, Audio, Volume, Graphics, Quality ===");
        Console.WriteLine();

        string[] keywords = {
            "Menu", "Button", "Setting", "Local", "Panel", "UI", "Config",
            "Save", "Load", "Option", "Preference", "Language", "Input",
            "Keybind", "Resolution", "Audio", "Volume", "Graphics", "Quality",
            "Slider", "Toggle", "Dropdown", "Dialog", "Popup", "Screen",
            "HUD", "Tooltip", "Tab", "Window", "Lobby", "Server", "Network",
            "Player", "Game", "Manager", "Controller", "Handler", "System"
        };

        var filtered = sorted
            .Where(t => { try { return keywords.Any(kw => (t.Name ?? "").IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0); } catch { return false; } })
            .ToList();

        Console.WriteLine($"Matching types: {filtered.Count}");
        Console.WriteLine();

        foreach (var t in filtered)
        {
            string kind, baseName = "", vis = "";
            try
            {
                kind = t.IsEnum ? "enum" :
                       t.IsInterface ? "interface" :
                       t.IsValueType ? "struct" :
                       t.IsClass ? "class" : "type";
            }
            catch { kind = "type"; }

            try
            {
                if (t.BaseType != null)
                    baseName = $" : {t.BaseType.FullName ?? t.BaseType.Name}";
            }
            catch { }

            try
            {
                vis = t.IsPublic || t.IsNestedPublic ? "public" :
                      t.IsNestedPrivate ? "private" :
                      t.IsNestedFamily ? "protected" :
                      t.IsNotPublic ? "internal" : "";
            }
            catch { }

            try { Console.WriteLine($"  {vis} {kind} {t.FullName ?? t.Name}{baseName}"); }
            catch { try { Console.WriteLine($"  {vis} {kind} {t.Name}{baseName}"); } catch { } }

            // Show public members for interesting types
            try
            {
                var members = t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                var methods = members.Where(m => m.MemberType == MemberTypes.Method && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")).ToArray();
                var properties = members.Where(m => m.MemberType == MemberTypes.Property).ToArray();
                var fields = members.Where(m => m.MemberType == MemberTypes.Field).ToArray();

                if (fields.Length > 0)
                {
                    foreach (var f in fields.Take(20))
                    {
                        try
                        {
                            var fi = (FieldInfo)f;
                            Console.WriteLine($"      field: {fi.FieldType.Name} {fi.Name}");
                        }
                        catch { Console.WriteLine($"      field: {f.Name}"); }
                    }
                    if (fields.Length > 20) Console.WriteLine($"      ... and {fields.Length - 20} more fields");
                }

                if (properties.Length > 0)
                {
                    foreach (var p in properties.Take(20))
                    {
                        try
                        {
                            var pi = (PropertyInfo)p;
                            Console.WriteLine($"      prop: {pi.PropertyType.Name} {pi.Name}");
                        }
                        catch { Console.WriteLine($"      prop: {p.Name}"); }
                    }
                    if (properties.Length > 20) Console.WriteLine($"      ... and {properties.Length - 20} more properties");
                }

                if (methods.Length > 0)
                {
                    foreach (var m in methods.Take(30))
                    {
                        try
                        {
                            var mi = (MethodInfo)m;
                            var parms = string.Join(", ", mi.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"      method: {mi.ReturnType.Name} {mi.Name}({parms})");
                        }
                        catch { Console.WriteLine($"      method: {m.Name}(...)"); }
                    }
                    if (methods.Length > 30) Console.WriteLine($"      ... and {methods.Length - 30} more methods");
                }
            }
            catch { }

            Console.WriteLine();
        }
    }
}
