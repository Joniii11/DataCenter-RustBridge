using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DataCenterModLoader;

// safe game state accessors, returns defaults when singletons are null
public static class GameHooks
{
    public static int EnsureAllRackPositionUIDs()
    {
        try
        {
            var mgr = MainGameManager.instance;
            if (mgr == null)
            {
                CrashLog.Log("[WorldSync] EnsureAllRackPositionUIDs: MainGameManager is null");
                return 0;
            }

            var allPositions = UnityEngine.Object.FindObjectsOfType<RackPosition>();
            if (allPositions == null || allPositions.Count == 0)
            {
                CrashLog.Log("[WorldSync] EnsureAllRackPositionUIDs: no RackPositions found");
                return 0;
            }

            var sorted = new List<RackPosition>();
            foreach (var rp in allPositions)
            {
                if (rp != null) sorted.Add(rp);
            }

            sorted.Sort((a, b) =>
            {
                var pa = a.transform.position;
                var pb = b.transform.position;
                int cmp = pa.x.CompareTo(pb.x);
                if (cmp != 0) return cmp;
                cmp = pa.z.CompareTo(pb.z);
                if (cmp != 0) return cmp;
                cmp = pa.y.CompareTo(pb.y);
                if (cmp != 0) return cmp;
                cmp = a.positionIndex.CompareTo(b.positionIndex);
                if (cmp != 0) return cmp;

                string nameA = "", nameB = "";
                try { nameA = a.rack?.gameObject?.name ?? ""; } catch { }
                try { nameB = b.rack?.gameObject?.name ?? ""; } catch { }
                return string.Compare(nameA, nameB, StringComparison.Ordinal);
            });

            const int SYNC_UID_BASE = 10000;
            mgr.lastUsedRackPositionGlobalUID = SYNC_UID_BASE;

            int assigned = 0;
            foreach (var rp in sorted)
            {
                try
                {
                    mgr.lastUsedRackPositionGlobalUID++;
                    rp.rackPosGlobalUID = mgr.lastUsedRackPositionGlobalUID;
                    assigned++;
                }
                catch { /* field access can fail during teardown */ }
            }

            try
            {
                var servers = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Server>();
                int updated = 0;
                foreach (var srv in servers)
                {
                    try
                    {
                        if (srv.currentRackPosition != null)
                        {
                            int oldUid = srv.rackPositionUID;
                            int newUid = srv.currentRackPosition.rackPosGlobalUID;
                            if (oldUid != newUid)
                            {
                                srv.rackPositionUID = newUid;
                                updated++;
                            }
                        }
                    }
                    catch { }
                }
                if (updated > 0)
                    CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: updated {updated} server rackPositionUID references");
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: server ref update failed: {ex.Message}");
            }

            try
            {
                var switches = UnityEngine.Object.FindObjectsOfType<Il2Cpp.NetworkSwitch>();
                int swUpdated = 0;
                foreach (var sw in switches)
                {
                    try
                    {
                        if (sw.currentRackPosition != null)
                        {
                            int oldUid = sw.rackPositionUID;
                            int newUid = sw.currentRackPosition.rackPosGlobalUID;
                            if (oldUid != newUid)
                            {
                                sw.rackPositionUID = newUid;
                                swUpdated++;
                            }
                        }
                    }
                    catch { }
                }
                if (swUpdated > 0)
                    CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: updated {swUpdated} switch rackPositionUID references");
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: switch ref update failed: {ex.Message}");
            }

            try
            {
                var panels = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PatchPanel>();
                int ppUpdated = 0;
                foreach (var pp in panels)
                {
                    try
                    {
                        if (pp.currentRackPosition != null)
                        {
                            int oldUid = pp.rackPositionUID;
                            int newUid = pp.currentRackPosition.rackPosGlobalUID;
                            if (oldUid != newUid)
                            {
                                pp.rackPositionUID = newUid;
                                ppUpdated++;
                            }
                        }
                    }
                    catch { }
                }
                if (ppUpdated > 0)
                    CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: updated {ppUpdated} patchpanel rackPositionUID references");
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: patchpanel ref update failed: {ex.Message}");
            }

            CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs: assigned {assigned}/{sorted.Count} positions (counter now {mgr.lastUsedRackPositionGlobalUID})");
            return assigned;
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[WorldSync] EnsureAllRackPositionUIDs failed: {ex.Message}");
            return 0;
        }
    }

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
            int total = techs.Count;
            if (total == 0) return 0;

            // Primary: use GetActiveJobs() — counts all busy techs across all 6 slots
            try
            {
                var activeJobs = tm.GetActiveJobs();
                int activeCount = activeJobs != null ? activeJobs.Count : 0;
                return (uint)Math.Max(0, total - activeCount);
            }
            catch { }

            // Fallback: iterate isBusy per-technician (pre-update behaviour)
            uint count = 0;
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
            // Return the exact count of Technician objects — all 6 when all are hired/active.
            // CommandCenterOperator entries live in tm.commandCenterOperator (separate list)
            // and cannot do physical repairs, so they are intentionally excluded here.
            return (uint)Math.Max(0, techs.Count);
        }
        catch { return 0; }
    }

    public static uint GetQueuedJobCount()
    {
        try
        {
            var tm = TechnicianManager.instance;
            if (tm == null) return 0;
            return (uint)Math.Max(0, tm.QueuedJobCount);
        }
        catch { return 0; }
    }

    /// <summary>
    /// The new game update added <c>CommandCenterOperator</c> NPCs that must be hired before
    /// <c>ProcessDispatchQueue</c> will move jobs from <c>pendingDispatches</c> to actual
    /// technicians.  If no operator is hired the queue grows forever while techs stand idle.
    ///
    /// This method bypasses that requirement: it drains <c>pendingDispatches</c> directly and
    /// calls <c>Technician.AssignJob</c> on every free technician we can find.  It is called
    /// immediately after every <c>SendTechnician</c> so the SysAdmin mod keeps working even
    /// without a hired Command-Center Operator.
    /// </summary>
    public static void ForceProcessPendingQueue(TechnicianManager tm)
    {
        try
        {
            var pending = tm.pendingDispatches;
            if (pending == null || pending.Count == 0) return;

            var techs = tm.technicians;
            if (techs == null || techs.Count == 0) return;

            // Build active-technician set via GetActiveJobs() for accuracy
            var activeTechIds = new System.Collections.Generic.HashSet<int>();
            try
            {
                var activeJobs = tm.GetActiveJobs();
                if (activeJobs != null)
                {
                    foreach (var aj in activeJobs)
                    {
                        try
                        {
                            if (aj.assignedTechnician != null)
                                activeTechIds.Add(aj.assignedTechnician.technicianID);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            int assigned = 0;
            for (int i = 0; i < techs.Count && pending.Count > 0; i++)
            {
                try
                {
                    var tech = techs[i];
                    if (tech == null) continue;

                    // Skip techs that are already working
                    bool busy = activeTechIds.Contains(tech.technicianID);
                    if (!busy)
                    {
                        try { busy = tech.isBusy; } catch { }
                    }
                    if (busy) continue;

                    // Dequeue next pending job and assign directly
                    var job = pending.Dequeue();
                    tech.AssignJob(job);
                    assigned++;

                    try
                    {
                        CrashLog.Log($"ForceProcessPendingQueue: assigned '{job.DeviceName}' → tech #{tech.technicianID} ({tech.technicianName})");
                    }
                    catch { }
                }
                catch { }
            }

            if (assigned > 0)
                CrashLog.Log($"ForceProcessPendingQueue: force-assigned {assigned} job(s) (bypassed CommandCenterOperator check)");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ForceProcessPendingQueue", ex);
        }
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

            int skipped = 0;
            foreach (var key in keys)
            {
                try
                {
                    Server server;
                    try { server = dict[key]; } catch { continue; }
                    if (server == null) continue;

                    if (tm.IsDeviceAlreadyAssigned(null, server)) { skipped++; continue; }

                    tm.SendTechnician(null, server);
                    ForceProcessPendingQueue(tm);
                    return 1;
                }
                catch { }
            }
            if (skipped > 0)
                CrashLog.Log($"DispatchRepairServer: no target — {skipped}/{keys.Count} device(s) already assigned in queue");
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

            int skipped = 0;
            foreach (var key in keys)
            {
                try
                {
                    NetworkSwitch sw;
                    try { sw = dict[key]; } catch { continue; }
                    if (sw == null) continue;

                    if (tm.IsDeviceAlreadyAssigned(sw, null)) { skipped++; continue; }

                    tm.SendTechnician(sw, null);
                    ForceProcessPendingQueue(tm);
                    return 1;
                }
                catch { }
            }
            if (skipped > 0)
                CrashLog.Log($"DispatchRepairSwitch: no target — {skipped}/{keys.Count} device(s) already assigned in queue");
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

            int skipped = 0;
            foreach (var key in keys)
            {
                try
                {
                    Server server;
                    try { server = dict[key]; } catch { continue; }
                    if (server == null) continue;
                    if (server.isBroken) continue;
                    if (server.eolTime > 0) continue;

                    if (tm.IsDeviceAlreadyAssigned(null, server)) { skipped++; continue; }

                    tm.SendTechnician(null, server);
                    ForceProcessPendingQueue(tm);
                    return 1;
                }
                catch { }
            }
            if (skipped > 0)
                CrashLog.Log($"DispatchReplaceServer: no target — {skipped}/{keys.Count} device(s) already assigned in queue");
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

            int skipped = 0;
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

                    if (tm.IsDeviceAlreadyAssigned(sw, null)) { skipped++; continue; }

                    tm.SendTechnician(sw, null);
                    ForceProcessPendingQueue(tm);
                    return 1;
                }
                catch { }
            }
            if (skipped > 0)
                CrashLog.Log($"DispatchReplaceSwitch: no target — {skipped}/{keys.Count} device(s) already assigned in queue");
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
