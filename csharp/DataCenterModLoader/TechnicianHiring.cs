using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace DataCenterModLoader;

/// <summary>
/// Manages 7 extra hireable technician employees.
/// Registers them with <see cref="CustomEmployeeManager"/> and handles spawning/despawning
/// cloned Technician GameObjects when they are hired or fired.
/// </summary>
public static class TechnicianHiring
{
    // ── Inner definition type ───────────────────────────────────────────

    private readonly struct TechDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;
        public readonly float Salary;
        public readonly float RequiredRep;

        public TechDef(string id, string name, string description, float salary, float requiredRep)
        {
            Id = id;
            Name = name;
            Description = description;
            Salary = salary;
            RequiredRep = requiredRep;
        }
    }

    // ── The 7 technician definitions ────────────────────────────────────

    private static readonly List<TechDef> _definitions = new()
    {
        new TechDef("tech_extra_1", "Junior Technician I",   "A fresh hire eager to learn the ropes. Slow but cheap.",       1500f,    0f),
        new TechDef("tech_extra_2", "Junior Technician II",  "A second pair of hands. Still learning.",                       2000f,  100f),
        new TechDef("tech_extra_3", "Technician III",        "A reliable technician with solid skills.",                      3000f,  250f),
        new TechDef("tech_extra_4", "Technician IV",         "An experienced technician who gets the job done.",              4000f,  500f),
        new TechDef("tech_extra_5", "Senior Technician V",   "A senior technician. Fast and efficient.",                      5500f,  800f),
        new TechDef("tech_extra_6", "Senior Technician VI",  "A highly skilled senior technician.",                           7000f, 1200f),
        new TechDef("tech_extra_7", "Lead Technician",       "The best in the business. Worth every penny.",                  9000f, 1800f),
    };

    // ── State ───────────────────────────────────────────────────────────

    /// <summary>Tracks technicianIDs of technicians we have spawned so we can fire (remove) them in LIFO order.</summary>
    private static readonly List<int> _spawnedTechIds = new();

    /// <summary>Next technician ID to assign. Starts at 1000 to avoid collisions with the game's built-in technicians.</summary>
    private static int _nextTechId = 1000;

    /// <summary>Whether <see cref="Initialize"/> has already been called.</summary>
    private static bool _initialized = false;

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers all 7 technician employee definitions with <see cref="CustomEmployeeManager"/>.
    /// Safe to call more than once — subsequent calls are ignored.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        try
        {
            CrashLog.Log("TechnicianHiring.Initialize: registering 7 extra technician employees");

            foreach (var def in _definitions)
            {
                try
                {
                    int result = CustomEmployeeManager.Register(
                        def.Id,
                        def.Name,
                        def.Description,
                        def.Salary,
                        def.RequiredRep,
                        requiresConfirmation: true
                    );

                    CrashLog.Log($"TechnicianHiring: registered '{def.Id}' ({def.Name}) — result={result}");
                    Core.Instance?.LoggerInstance.Msg(
                        $"[TechnicianHiring] Registered: {def.Name} (salary={def.Salary}/h, rep={def.RequiredRep})");
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"TechnicianHiring.Initialize: failed to register '{def.Id}'", ex);
                }
            }

            _initialized = true;
            CrashLog.Log("TechnicianHiring.Initialize: complete");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("TechnicianHiring.Initialize", ex);
        }
    }

    /// <summary>
    /// Called when ANY custom employee is hired. If the <paramref name="employeeId"/> belongs to
    /// one of our technicians (starts with <c>"tech_extra_"</c>), we clone an existing in-game
    /// technician and register it with <see cref="TechnicianManager"/>.
    /// </summary>
    public static void OnEmployeeHired(string employeeId)
    {
        try
        {
            if (string.IsNullOrEmpty(employeeId) || !employeeId.StartsWith("tech_extra_"))
                return;

            CrashLog.Log($"TechnicianHiring.OnEmployeeHired: handling '{employeeId}'");

            var tm = TechnicianManager.instance;
            if (tm == null)
            {
                CrashLog.Log("TechnicianHiring.OnEmployeeHired: TechnicianManager.instance is null — aborting");
                Core.Instance?.LoggerInstance.Error("[TechnicianHiring] TechnicianManager not available");
                return;
            }

            var existing = tm.technicians;
            if (existing == null || existing.Count == 0)
            {
                CrashLog.Log("TechnicianHiring.OnEmployeeHired: No existing technicians to clone");
                Core.Instance?.LoggerInstance.Error("[TechnicianHiring] No existing technicians to clone");
                return;
            }

            int existingCount = existing.Count;

            // Cycle through existing technicians to pick a model (greg / john / woman rotation)
            int sourceIndex = _spawnedTechIds.Count % existingCount;
            var source = existing[sourceIndex];
            if (source == null)
            {
                CrashLog.Log($"TechnicianHiring.OnEmployeeHired: source technician at index {sourceIndex} is null");
                return;
            }

            CrashLog.Log($"TechnicianHiring.OnEmployeeHired: cloning technician at index {sourceIndex} ('{source.technicianName}')");

            // Clone the entire GameObject
            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            if (clone == null)
            {
                CrashLog.Log("TechnicianHiring.OnEmployeeHired: Object.Instantiate returned null");
                return;
            }

            var tech = clone.GetComponent<Technician>();
            if (tech == null)
            {
                CrashLog.Log("TechnicianHiring.OnEmployeeHired: cloned object has no Technician component");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            // Assign a unique ID that won't collide with built-in technicians
            int newId = _nextTechId++;
            tech.technicianID = newId;
            tech.technicianName = "Mod Tech " + newId;

            // Salary is handled by CustomEmployeeManager, not the game's built-in salary system
            tech.salary = 0;

            // Wire up shared transforms from TechnicianManager
            tech.transformContainer = tm.transformContainer;
            tech.transformDumpster = tm.transformDumpster;
            tech.transformDeviceSpawnPosition = tm.transformDeviceSpawnPosition;

            // Assign an idle position (cycle through available transforms)
            if (tm.transformIdle != null && tm.transformIdle.Length > 0)
            {
                int idleIndex = _spawnedTechIds.Count % tm.transformIdle.Length;
                tech.transformIdle = tm.transformIdle[idleIndex];
                CrashLog.Log($"TechnicianHiring.OnEmployeeHired: assigned idle transform index {idleIndex}");
            }
            else
            {
                CrashLog.Log("TechnicianHiring.OnEmployeeHired: no idle transforms available on TechnicianManager");
            }

            // Register with the manager
            tm.AddTechnician(tech);
            _spawnedTechIds.Add(newId);

            CrashLog.Log($"TechnicianHiring.OnEmployeeHired: spawned technician id={newId} for employee '{employeeId}' (total spawned: {_spawnedTechIds.Count})");
            Core.Instance?.LoggerInstance.Msg($"[TechnicianHiring] Spawned technician #{newId} for '{employeeId}'");
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"TechnicianHiring.OnEmployeeHired({employeeId})", ex);
        }
    }

    /// <summary>
    /// Called when ANY custom employee is fired. If the <paramref name="employeeId"/> belongs to
    /// one of our technicians, we pop the most recently spawned technician and fire it via
    /// <see cref="TechnicianManager.FireTechnician"/>.
    /// </summary>
    public static void OnEmployeeFired(string employeeId)
    {
        try
        {
            if (string.IsNullOrEmpty(employeeId) || !employeeId.StartsWith("tech_extra_"))
                return;

            CrashLog.Log($"TechnicianHiring.OnEmployeeFired: handling '{employeeId}'");

            if (_spawnedTechIds.Count == 0)
            {
                CrashLog.Log("TechnicianHiring.OnEmployeeFired: no spawned technicians to remove — ignoring");
                Core.Instance?.LoggerInstance.Warning("[TechnicianHiring] No spawned technicians to fire");
                return;
            }

            // Pop the last (most recently hired) technician
            var id = _spawnedTechIds[^1];
            _spawnedTechIds.RemoveAt(_spawnedTechIds.Count - 1);

            CrashLog.Log($"TechnicianHiring.OnEmployeeFired: firing technician id={id} (remaining: {_spawnedTechIds.Count})");

            TechnicianManager.instance?.FireTechnician(id);

            CrashLog.Log($"TechnicianHiring.OnEmployeeFired: technician id={id} fired successfully");
            Core.Instance?.LoggerInstance.Msg($"[TechnicianHiring] Fired technician #{id} for '{employeeId}'");
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"TechnicianHiring.OnEmployeeFired({employeeId})", ex);
        }
    }

    /// <summary>
    /// Called after a game save is loaded. Checks which of the 7 technician employees are currently
    /// marked as hired in <see cref="CustomEmployeeManager"/> and re-spawns them.
    /// This handles the case where the player reloads a save with extra technicians already hired.
    /// </summary>
    public static void RestoreOnLoad()
    {
        try
        {
            CrashLog.Log("TechnicianHiring.RestoreOnLoad: checking for previously hired technicians");

            // Clear any existing spawned state — we are rebuilding from saved hire flags
            _spawnedTechIds.Clear();

            int restored = 0;
            foreach (var def in _definitions)
            {
                try
                {
                    if (CustomEmployeeManager.IsHired(def.Id))
                    {
                        CrashLog.Log($"TechnicianHiring.RestoreOnLoad: '{def.Id}' is hired — re-spawning");
                        OnEmployeeHired(def.Id);
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"TechnicianHiring.RestoreOnLoad: failed to restore '{def.Id}'", ex);
                }
            }

            CrashLog.Log($"TechnicianHiring.RestoreOnLoad: restored {restored} technician(s)");
            if (restored > 0)
            {
                Core.Instance?.LoggerInstance.Msg($"[TechnicianHiring] Restored {restored} extra technician(s) from save");
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("TechnicianHiring.RestoreOnLoad", ex);
        }
    }
}
