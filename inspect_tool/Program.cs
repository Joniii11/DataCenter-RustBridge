using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static MetadataLoadContext mlc = null!;

    static void Main(string[] args)
    {
        string asmDir = @"C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies";
        string melonNet6Dir = @"C:\Program Files (x86)\Steam\steamapps\common\Data Center\MelonLoader\net6";

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
        mlc = new MetadataLoadContext(resolver);

        // ── 1) Inspect Assembly-CSharp.dll (original behavior) ──
        string assemblyCSharp = Path.Combine(asmDir, "Assembly-CSharp.dll");
        if (File.Exists(assemblyCSharp))
        {
            InspectAssemblyCSharp(assemblyCSharp);
        }
        else
        {
            Console.Error.WriteLine($"File not found: {assemblyCSharp}");
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 120));
        Console.WriteLine();

        // ── 2) Inspect Il2CppUMA_Core.dll ──
        string umaDll = Path.Combine(asmDir, "Il2CppUMA_Core.dll");
        if (File.Exists(umaDll))
        {
            InspectUmaCore(umaDll);
        }
        else
        {
            Console.Error.WriteLine($"File not found: {umaDll}");
        }

        mlc.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Assembly-CSharp inspection  (kept from original)
    // ═══════════════════════════════════════════════════════════════════
    static void InspectAssemblyCSharp(string targetDll)
    {
        Assembly asm;
        try { asm = mlc.LoadFromAssemblyPath(targetDll); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to load Assembly-CSharp: {ex.Message}"); return; }

        Type[] allTypes = SafeGetTypes(asm);

        Console.WriteLine($"=== ALL TYPES IN Assembly-CSharp.dll ({allTypes.Length} total) ===");
        Console.WriteLine();

        var sorted = allTypes.OrderBy(SafeFullName).ToList();
        foreach (var t in sorted)
            PrintTypeSummaryLine(t);

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
            PrintTypeWithMembers(t, maxFields: 20, maxProps: 20, maxMethods: 30);
            Console.WriteLine();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UMA Core inspection  –  deep dump of character-generation types
    // ═══════════════════════════════════════════════════════════════════
    static void InspectUmaCore(string targetDll)
    {
        Assembly asm;
        try { asm = mlc.LoadFromAssemblyPath(targetDll); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to load UMA_Core: {ex.Message}"); return; }

        Type[] allTypes = SafeGetTypes(asm);

        Console.WriteLine($"=== ALL TYPES IN Il2CppUMA_Core.dll ({allTypes.Length} total) ===");
        Console.WriteLine();

        var sorted = allTypes.OrderBy(SafeFullName).ToList();
        foreach (var t in sorted)
            PrintTypeSummaryLine(t);

        // ── Key types we want full dumps of ──
        string[] deepDumpTypes = {
            "DynamicCharacterAvatar",
            "UMAData",
            "UMAGenerator",
            "UMAGeneratorBase",
            "UMAGeneratorBuiltin",
            "UMAGeneratorCoroutine",
            "UMAGeneratorGLIB",
            "UMASkeleton",
            "UMASkeletonDefault",
            "UMAMeshData",
            "UMAMeshCombiner",
            "SlotData",
            "SlotDataAsset",
            "OverlayData",
            "OverlayDataAsset",
            "RaceData",
            "UMARecipeBase",
            "UMATextRecipe",
            "UMAPackedRecipeBase",
            "UMADnaBase",
            "UMADnaConverter",
            "DynamicDNAConverterBehaviour",
            "DynamicUMADna",
            "UMAContext",
            "UMAContextBase",
            "UMAAssetIndexer",
            "DynamicCharacterSystem",
            "UMABonePose",
            "UMARendererAsset",
            "UMAAvatarBase",
            "CharacterUpdated",
            "UMAExpressionPlayer",
            "UMAExpressionSet",
            "MeshHideAsset",
            "SkinnedMeshCombiner",
        };

        // ── Keyword-based filter for anything else interesting ──
        string[] umaKeywords = {
            "Character", "Avatar", "Generator", "Skeleton", "Mesh",
            "Slot", "Overlay", "Race", "Recipe", "Dna", "DNA",
            "Wardrobe", "Combiner", "Renderer", "BonePose", "Expression",
            "UMAData", "Context", "Indexer", "Build", "Generate",
            "MeshHide", "Skinned", "TextureMerge", "AtlasMaterial",
        };

        Console.WriteLine();
        Console.WriteLine("=== UMA DEEP DUMP: Key character-generation types ===");
        Console.WriteLine();

        // Collect types to deep-dump (exact name match OR keyword match)
        var deepSet = new HashSet<string>(deepDumpTypes, StringComparer.OrdinalIgnoreCase);
        var toDump = sorted.Where(t =>
        {
            string name = SafeName(t);
            if (deepSet.Contains(name)) return true;
            return umaKeywords.Any(kw => name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
        }).ToList();

        Console.WriteLine($"Types matching deep-dump criteria: {toDump.Count}");
        Console.WriteLine();

        foreach (var t in toDump)
        {
            // Full dump with generous limits
            PrintTypeWithMembers(t, maxFields: 100, maxProps: 100, maxMethods: 200);
            Console.WriteLine();
        }

        // ── Extra: search ALL types for specific method names ──
        string[] methodsOfInterest = {
            "GenerateSingleUMA", "Initialize", "BuildCharacter",
            "CharacterCreated", "CharacterUpdated", "CharacterDestroyed",
            "UpdateUMAMesh", "UpdateNewUMAMesh", "ApplyDNA",
            "GenerateUMAShapes", "UpdateSlot", "UpdateSameRace",
            "UpdateNewRace", "ForceUpdate", "BuildMeshDefinition",
            "CombineMeshes", "UpdateUMABody", "Load", "SetSlot",
            "SetRace", "ChangeRace", "LoadFromRecipe", "SaveToRecipe",
            "Preload", "OnCharacterCreated", "OnCharacterUpdated",
        };

        Console.WriteLine("=== CROSS-TYPE METHOD SEARCH ===");
        Console.WriteLine();

        var methodSet = new HashSet<string>(methodsOfInterest, StringComparer.OrdinalIgnoreCase);

        foreach (var t in sorted)
        {
            try
            {
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                var hits = methods.Where(m => methodSet.Contains(m.Name)).ToArray();
                if (hits.Length > 0)
                {
                    Console.WriteLine($"  [{SafeFullName(t)}]");
                    foreach (var mi in hits)
                    {
                        try
                        {
                            string vis = mi.IsPublic ? "public" : mi.IsFamily ? "protected" : mi.IsPrivate ? "private" : "internal";
                            string stat = mi.IsStatic ? " static" : "";
                            var parms = string.Join(", ", mi.GetParameters().Select(p =>
                            {
                                try { return $"{p.ParameterType.Name} {p.Name}"; } catch { return p.Name ?? "?"; }
                            }));
                            string ret = "void";
                            try { ret = mi.ReturnType.Name; } catch { }
                            Console.WriteLine($"      {vis}{stat} {ret} {mi.Name}({parms})");
                        }
                        catch { Console.WriteLine($"      {mi.Name}(...)"); }
                    }
                    Console.WriteLine();
                }
            }
            catch { }
        }

        // ── Also dump any events / delegates related to character lifecycle ──
        Console.WriteLine("=== EVENTS & DELEGATES ===");
        Console.WriteLine();

        foreach (var t in sorted)
        {
            try
            {
                var events = t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (events.Length > 0)
                {
                    Console.WriteLine($"  [{SafeFullName(t)}]");
                    foreach (var ev in events)
                    {
                        try
                        {
                            Console.WriteLine($"      event {ev.EventHandlerType?.Name ?? "?"} {ev.Name}");
                        }
                        catch { Console.WriteLine($"      event {ev.Name}"); }
                    }
                    Console.WriteLine();
                }
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    static Type[] SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            Console.Error.WriteLine($"Partial type load. {ex.LoaderExceptions.Length} loader exceptions (showing first 5):");
            foreach (var le in ex.LoaderExceptions.Take(5))
                Console.Error.WriteLine($"  {le?.Message}");
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }

    static string SafeName(Type t)
    {
        try { return t.Name ?? "?"; } catch { return "?"; }
    }

    static string SafeFullName(Type t)
    {
        try { return t.FullName ?? t.Name ?? "?"; } catch { try { return t.Name ?? "?"; } catch { return "?"; } }
    }

    static void PrintTypeSummaryLine(Type t)
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

    static void PrintTypeWithMembers(Type t, int maxFields = 20, int maxProps = 20, int maxMethods = 30)
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

        // Print interfaces
        string ifaces = "";
        try
        {
            var interfaces = t.GetInterfaces();
            if (interfaces.Length > 0)
                ifaces = " [implements: " + string.Join(", ", interfaces.Select(i => { try { return i.Name; } catch { return "?"; } })) + "]";
        }
        catch { }

        try { Console.WriteLine($"  {vis} {kind} {t.FullName ?? t.Name}{baseName}{ifaces}"); }
        catch { try { Console.WriteLine($"  {vis} {kind} {t.Name}{baseName}{ifaces}"); } catch { } }

        // Show ALL public + protected members (instance + static, declared only)
        try
        {
            var bindingAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var members = t.GetMembers(bindingAll);

            var fields = members.Where(m => m.MemberType == MemberTypes.Field).ToArray();
            var properties = members.Where(m => m.MemberType == MemberTypes.Property).ToArray();
            var methods = members.Where(m => m.MemberType == MemberTypes.Method && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_")).ToArray();
            var events = members.Where(m => m.MemberType == MemberTypes.Event).ToArray();
            var nested = members.Where(m => m.MemberType == MemberTypes.NestedType).ToArray();

            if (fields.Length > 0)
            {
                Console.WriteLine($"    -- Fields ({fields.Length}) --");
                foreach (var f in fields.Take(maxFields))
                {
                    try
                    {
                        var fi = (FieldInfo)f;
                        string fvis = fi.IsPublic ? "public" : fi.IsFamily ? "protected" : fi.IsPrivate ? "private" : "internal";
                        string fstat = fi.IsStatic ? " static" : "";
                        Console.WriteLine($"      {fvis}{fstat} {fi.FieldType.Name} {fi.Name}");
                    }
                    catch { Console.WriteLine($"      {f.Name}"); }
                }
                if (fields.Length > maxFields) Console.WriteLine($"      ... and {fields.Length - maxFields} more fields");
            }

            if (events.Length > 0)
            {
                Console.WriteLine($"    -- Events ({events.Length}) --");
                foreach (var e in events)
                {
                    try
                    {
                        var ei = (EventInfo)e;
                        Console.WriteLine($"      event {ei.EventHandlerType?.Name ?? "?"} {ei.Name}");
                    }
                    catch { Console.WriteLine($"      event {e.Name}"); }
                }
            }

            if (properties.Length > 0)
            {
                Console.WriteLine($"    -- Properties ({properties.Length}) --");
                foreach (var p in properties.Take(maxProps))
                {
                    try
                    {
                        var pi = (PropertyInfo)p;
                        string pget = pi.CanRead ? "get;" : "";
                        string pset = pi.CanWrite ? "set;" : "";
                        Console.WriteLine($"      {pi.PropertyType.Name} {pi.Name} {{ {pget} {pset} }}");
                    }
                    catch { Console.WriteLine($"      prop: {p.Name}"); }
                }
                if (properties.Length > maxProps) Console.WriteLine($"      ... and {properties.Length - maxProps} more properties");
            }

            if (methods.Length > 0)
            {
                Console.WriteLine($"    -- Methods ({methods.Length}) --");
                foreach (var m in methods.Take(maxMethods))
                {
                    try
                    {
                        var mi = (MethodInfo)m;
                        string mvis = mi.IsPublic ? "public" : mi.IsFamily ? "protected" : mi.IsPrivate ? "private" : "internal";
                        string mstat = mi.IsStatic ? " static" : "";
                        string mvirt = mi.IsVirtual ? " virtual" : "";
                        var parms = string.Join(", ", mi.GetParameters().Select(p =>
                        {
                            try { return $"{p.ParameterType.Name} {p.Name}"; } catch { return p.Name ?? "?"; }
                        }));
                        string ret = "void";
                        try { ret = mi.ReturnType.Name; } catch { }
                        Console.WriteLine($"      {mvis}{mstat}{mvirt} {ret} {mi.Name}({parms})");
                    }
                    catch { Console.WriteLine($"      method: {m.Name}(...)"); }
                }
                if (methods.Length > maxMethods) Console.WriteLine($"      ... and {methods.Length - maxMethods} more methods");
            }

            if (nested.Length > 0)
            {
                Console.WriteLine($"    -- Nested types ({nested.Length}) --");
                foreach (var n in nested)
                {
                    try { Console.WriteLine($"      {n.Name}"); }
                    catch { }
                }
            }
        }
        catch { }
    }
}
