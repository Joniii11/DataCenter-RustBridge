using System;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DataCenterModLoader;

// safe game state accessors, returns defaults when singletons are null
public static class GameHooks
{
    public static float GetPlayerMoney()
    {
        try { return PlayerManager.instance?.playerClass?.money ?? 0f; }
        catch { return 0f; }
    }

    public static void SetPlayerMoney(float value)
    {
        try
        {
            var player = PlayerManager.instance?.playerClass;
            if (player != null) player.money = value;
        }
        catch { }
    }

    public static float GetPlayerXP()
    {
        try { return PlayerManager.instance?.playerClass?.xp ?? 0f; }
        catch { return 0f; }
    }

    public static void SetPlayerXP(float value)
    {
        try
        {
            var player = PlayerManager.instance?.playerClass;
            if (player != null) player.xp = value;
        }
        catch { }
    }

    public static float GetPlayerReputation()
    {
        try { return PlayerManager.instance?.playerClass?.reputation ?? 0f; }
        catch { return 0f; }
    }

    public static void SetPlayerReputation(float value)
    {
        try
        {
            var player = PlayerManager.instance?.playerClass;
            if (player != null) player.reputation = value;
        }
        catch { }
    }

    public static float GetTimeOfDay()
    {
        try { return TimeController.instance?.currentTimeOfDay ?? 0f; }
        catch { return 0f; }
    }

    public static int GetDay()
    {
        try { return TimeController.instance?.day ?? 0; }
        catch { return 0; }
    }

    public static float GetSecondsInFullDay()
    {
        try { return TimeController.instance?.secondsInFullDay ?? 0f; }
        catch { return 0f; }
    }

    public static void SetSecondsInFullDay(float value)
    {
        try
        {
            var tc = TimeController.instance;
            if (tc != null) tc.secondsInFullDay = value;
        }
        catch { }
    }

    public static int[] GetDeviceCounts()
    {
        try
        {
            var nm = NetworkMap.instance;
            if (nm == null) return Array.Empty<int>();
            Il2CppStructArray<int> arr = nm.GetNumberOfDevices();
            if (arr == null) return Array.Empty<int>();
            int[] result = new int[arr.Length];
            for (int i = 0; i < arr.Length; i++) result[i] = arr[i];
            return result;
        }
        catch { return Array.Empty<int>(); }
    }

    public static uint GetServerCount()
    {
        var counts = GetDeviceCounts();
        return counts.Length > 0 ? (uint)Math.Max(0, counts[0]) : 0;
    }

    public static uint GetSwitchCount()
    {
        var counts = GetDeviceCounts();
        return counts.Length > 1 ? (uint)Math.Max(0, counts[1]) : 0;
    }

    public static uint GetRackCount()
    {
        try
        {
            var racks = UnityEngine.Object.FindObjectsOfType<Rack>();
            return racks != null ? (uint)racks.Length : 0;
        }
        catch { return 0; }
    }

    public static int GetSatisfiedCustomerCount()
    {
        try { return CustomerBase.satisfiedCustomerCount; }
        catch { return 0; }
    }

    // Technician & Device management

    public static uint GetBrokenServerCount()
    {
        try
        {
            var nm = NetworkMap.instance;
            if (nm == null) return 0;
            var dict = nm.brokenServers;
            if (dict == null) return 0;
            return (uint)Math.Max(0, dict.Count);
        }
        catch { return 0; }
    }

    public static uint GetBrokenSwitchCount()
    {
        try
        {
            var nm = NetworkMap.instance;
            if (nm == null) return 0;
            var dict = nm.brokenSwitches;
            if (dict == null) return 0;
            return (uint)Math.Max(0, dict.Count);
        }
        catch { return 0; }
    }

    public static uint GetEolServerCount()
    {
        try
        {
            var nm = NetworkMap.instance;
            if (nm == null) return 0;
            var dict = nm.servers;
            if (dict == null) return 0;

            uint count = 0;
            // copy keys first to avoid Il2Cpp iteration issues
            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            foreach (var key in keys)
            {
                try
                {
                    var server = dict[key];
                    if (server == null) continue;
                    if (server.isBroken) continue;
                    // eolTime counts down; <= 0 means at/past EOL
                    if (server.eolTime <= 0) count++;
                }
                catch { }
            }
            return count;
        }
        catch { return 0; }
    }

    private static int _eolSwitchDiagCounter = 0;

    public static uint GetEolSwitchCount()
    {
        try
        {
            var nm = NetworkMap.instance;
            if (nm == null) return 0;
            var dict = nm.switches;
            if (dict == null) return 0;

            uint count = 0;
            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            foreach (var key in keys)
            {
                try
                {
                    var sw = dict[key];
                    if (sw == null) continue;
                    if (sw.isBroken) continue;
                    // Check both warning signs AND eolTime countdown (like servers)
                    bool isEol = sw.existingWarningSigns > 0;
                    if (!isEol)
                    {
                        try { isEol = sw.eolTime <= 0; } catch { }
                    }
                    if (isEol) count++;
                }
                catch { }
            }

            // Periodic diagnostic dump when EOL switches exist (every ~30s = 6 scans)
            if (count > 0)
            {
                _eolSwitchDiagCounter++;
                if (_eolSwitchDiagCounter >= 6)
                {
                    _eolSwitchDiagCounter = 0;
                    DumpSwitchDiagnostics();
                }
            }
            else
            {
                _eolSwitchDiagCounter = 0;
            }

            return count;
        }
        catch { return 0; }
    }

    public static uint GetFreeTechnicianCount()
    {
        try
        {
            var tm = TechnicianManager.instance;
            if (tm == null) return 0;
            var techs = tm.technicians;
            if (techs == null) return 0;

            uint count = 0;
            int total = techs.Count;
            for (int i = 0; i < total; i++)
            {
                try
                {
                    var tech = techs[i];
                    if (tech != null && !tech.isBusy) count++;
                }
                catch { }
            }
            return count;
        }
        catch { return 0; }
    }

    public static uint GetTotalTechnicianCount()
    {
        try
        {
            var tm = TechnicianManager.instance;
            if (tm == null) return 0;
            var techs = tm.technicians;
            if (techs == null) return 0;
            return (uint)Math.Max(0, techs.Count);
        }
        catch { return 0; }
    }

    // Returns: 1 = dispatched, 0 = no target, -1 = no free technician
    public static int DispatchRepairServer()
    {
        try
        {
            var nm = NetworkMap.instance;
            var tm = TechnicianManager.instance;
            if (nm == null || tm == null) return 0;

            if (GetFreeTechnicianCount() == 0) return -1;

            var dict = nm.brokenServers;
            if (dict == null || dict.Count == 0) return 0;

            // copy keys to avoid iteration issues
            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            foreach (var key in keys)
            {
                try
                {
                    Server server;
                    try { server = dict[key]; } catch { continue; }
                    if (server == null) continue;

                    if (tm.IsDeviceAlreadyAssigned(null, server)) continue;

                    tm.SendTechnician(null, server);
                    return 1;
                }
                catch { }
            }
            return 0;
        }
        catch { return 0; }
    }

    public static int DispatchRepairSwitch()
    {
        try
        {
            var nm = NetworkMap.instance;
            var tm = TechnicianManager.instance;
            if (nm == null || tm == null) return 0;

            if (GetFreeTechnicianCount() == 0) return -1;

            var dict = nm.brokenSwitches;
            if (dict == null || dict.Count == 0) return 0;

            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            foreach (var key in keys)
            {
                try
                {
                    NetworkSwitch sw;
                    try { sw = dict[key]; } catch { continue; }
                    if (sw == null) continue;

                    if (tm.IsDeviceAlreadyAssigned(sw, null)) continue;

                    tm.SendTechnician(sw, null);
                    return 1;
                }
                catch { }
            }
            return 0;
        }
        catch { return 0; }
    }

    public static int DispatchReplaceServer()
    {
        try
        {
            var nm = NetworkMap.instance;
            var tm = TechnicianManager.instance;
            if (nm == null || tm == null) return 0;

            if (GetFreeTechnicianCount() == 0) return -1;

            var dict = nm.servers;
            if (dict == null || dict.Count == 0) return 0;

            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            foreach (var key in keys)
            {
                try
                {
                    Server server;
                    try { server = dict[key]; } catch { continue; }
                    if (server == null) continue;
                    if (server.isBroken) continue;
                    if (server.eolTime > 0) continue; // not yet EOL

                    if (tm.IsDeviceAlreadyAssigned(null, server)) continue;

                    tm.SendTechnician(null, server);
                    return 1;
                }
                catch { }
            }
            return 0;
        }
        catch { return 0; }
    }

    public static int DispatchReplaceSwitch()
    {
        try
        {
            var nm = NetworkMap.instance;
            var tm = TechnicianManager.instance;
            if (nm == null || tm == null) return 0;

            if (GetFreeTechnicianCount() == 0) return -1;

            var dict = nm.switches;
            if (dict == null || dict.Count == 0) return 0;

            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            foreach (var key in keys)
            {
                try
                {
                    NetworkSwitch sw;
                    try { sw = dict[key]; } catch { continue; }
                    if (sw == null) continue;
                    if (sw.isBroken) continue;
                    // Check both warning signs AND eolTime countdown (like servers)
                    bool isEol = sw.existingWarningSigns > 0;
                    if (!isEol)
                    {
                        try { isEol = sw.eolTime <= 0; } catch { }
                    }
                    if (!isEol) continue; // not EOL

                    if (tm.IsDeviceAlreadyAssigned(sw, null)) continue;

                    tm.SendTechnician(sw, null);
                    return 1;
                }
                catch { }
            }
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Logs detailed per-switch diagnostics to CrashLog so we can identify
    /// which switch is missing from EOL detection.
    /// </summary>
    public static void DumpSwitchDiagnostics()
    {
        try
        {
            var nm = NetworkMap.instance;
            var tm = TechnicianManager.instance;
            if (nm == null) { MelonLoader.MelonLogger.Msg("[SwitchDiag] NetworkMap is null"); return; }

            var dict = nm.switches;
            if (dict == null) { MelonLoader.MelonLogger.Msg("[SwitchDiag] switches dict is null"); return; }

            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in dict) keys.Add(kvp.Key);

            MelonLoader.MelonLogger.Msg($"[SwitchDiag] --- {keys.Count} switch(es) in nm.switches ---");

            foreach (var key in keys)
            {
                try
                {
                    var sw = dict[key];
                    if (sw == null) { MelonLoader.MelonLogger.Msg($"[SwitchDiag]   key={key} => null"); continue; }

                    bool broken = false;
                    try { broken = sw.isBroken; } catch { }

                    int warningSigns = -999;
                    try { warningSigns = sw.existingWarningSigns; } catch { }

                    float eolTime = float.NaN;
                    try { eolTime = sw.eolTime; } catch { }

                    bool assigned = false;
                    try { if (tm != null) assigned = tm.IsDeviceAlreadyAssigned(sw, null); } catch { }

                    MelonLoader.MelonLogger.Msg(
                        $"[SwitchDiag]   key={key} broken={broken} warningSigns={warningSigns} eolTime={eolTime:F1} assigned={assigned}"
                    );
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Msg($"[SwitchDiag]   key={key} => exception: {ex.Message}");
                }
            }

            // Also check brokenSwitches dict
            var brokenDict = nm.brokenSwitches;
            int brokenCount = 0;
            if (brokenDict != null)
            {
                var brokenKeys = new System.Collections.Generic.List<string>();
                foreach (var kvp in brokenDict) brokenKeys.Add(kvp.Key);
                brokenCount = brokenKeys.Count;

                foreach (var key in brokenKeys)
                {
                    try
                    {
                        var sw = brokenDict[key];
                        float eolTime = float.NaN;
                        try { eolTime = sw.eolTime; } catch { }
                        int warningSigns = -999;
                        try { warningSigns = sw.existingWarningSigns; } catch { }

                        MelonLoader.MelonLogger.Msg(
                            $"[SwitchDiag]   BROKEN key={key} warningSigns={warningSigns} eolTime={eolTime:F1}"
                        );
                    }
                    catch { }
                }
            }

            MelonLoader.MelonLogger.Msg($"[SwitchDiag] --- total: {keys.Count} normal + {brokenCount} broken ---");
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Msg($"[SwitchDiag] exception: {ex.Message}");
        }
    }
}
