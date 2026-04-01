using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DataCenterModLoader;

// auto-repair system: scans for broken/EOL devices and dispatches technicians
// v2 — collect-then-dispatch pattern to avoid dictionary modification during iteration
public static class NetWatchSystem
{
    public static bool Enabled { get; set; }
    public static int DailySalary = 500;
    public static int TotalDispatches { get; private set; }
    public static int BrokenRepairs { get; private set; }
    public static int EolReplacements { get; private set; }

    private static FFIBridge _bridge;
    private static MelonLogger.Instance _logger;
    private static float _scanTimer;
    private static int _lastDay = -1;

    // how often we scan, in seconds
    private const float ScanInterval = 5f;



    // max dispatches per single scan cycle — be conservative to avoid overwhelming the game
    private const int MaxDispatchesPerScan = 1;

    // device type constants for events
    private const int DEVICE_SERVER = 0;
    private const int DEVICE_SWITCH = 1;

    // reason constants for events
    private const int REASON_BROKEN = 0;
    private const int REASON_EOL = 1;

    // a pending dispatch job collected during scan
    private struct DispatchJob
    {
        public NetworkSwitch Switch; // null for server jobs
        public Server Server;       // null for switch jobs
        public int DeviceType;      // 0=server, 1=switch
        public int Reason;          // 0=broken, 1=eol
        public string Label;        // for logging
    }

    public static void Initialize(FFIBridge bridge, MelonLogger.Instance logger)
    {
        _bridge = bridge;
        _logger = logger;
        _scanTimer = 0f;
        _lastDay = -1;
        TotalDispatches = 0;
        BrokenRepairs = 0;
        EolReplacements = 0;
        CrashLog.Log("NetWatchSystem initialized (v2 — collect-then-dispatch)");
    }

    public static void Update()
    {
        if (!Enabled) return;

        try
        {
            HandleSalary();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatchSystem.HandleSalary", ex);
        }

        _scanTimer += Time.deltaTime;
        if (_scanTimer < ScanInterval) return;
        _scanTimer = 0f;

        try
        {
            Scan();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatchSystem.Scan", ex);
        }
    }

    private static void HandleSalary()
    {
        var tc = TimeController.instance;
        if (tc == null) return;

        int day = tc.day;
        if (_lastDay < 0)
        {
            _lastDay = day;
            return;
        }

        if (day != _lastDay)
        {
            _lastDay = day;
            try
            {
                var pm = PlayerManager.instance;
                if (pm == null) return;
                var player = pm.playerClass;
                if (player != null)
                {
                    player.money -= DailySalary;
                    _logger?.Msg($"[NetWatch] Deducted daily salary: ${DailySalary}");
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("NetWatchSystem.DeductSalary", ex);
            }
        }
    }

    private static void Scan()
    {
        CrashLog.Log("NetWatch.Scan: starting scan cycle");

        NetworkMap networkMap;
        try
        {
            networkMap = NetworkMap.instance;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.Scan.GetNetworkMap", ex);
            return;
        }
        if (networkMap == null)
        {
            CrashLog.Log("NetWatch.Scan: NetworkMap.instance is null, skipping");
            return;
        }

        TechnicianManager techManager;
        try
        {
            techManager = TechnicianManager.instance;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.Scan.GetTechManager", ex);
            return;
        }
        if (techManager == null)
        {
            CrashLog.Log("NetWatch.Scan: TechnicianManager.instance is null, skipping");
            return;
        }

        // check if any technician is free before scanning
        if (!HasFreeTechnician(techManager))
        {
            CrashLog.Log("NetWatch.Scan: no free technicians, skipping scan");
            return;
        }

        // PHASE 1: Collect all dispatch candidates into a plain C# list.
        // We do NOT call any game-mutating methods during collection.
        var jobs = new List<DispatchJob>();

        CollectBrokenServers(networkMap, techManager, jobs);
        CollectBrokenSwitches(networkMap, techManager, jobs);
        CollectEolServers(networkMap, techManager, jobs);
        CollectEolSwitches(networkMap, techManager, jobs);

        CrashLog.Log($"NetWatch.Scan: collected {jobs.Count} dispatch candidate(s)");

        if (jobs.Count == 0) return;

        // PHASE 2: Dispatch (after all iteration is complete — no dictionary is being enumerated)
        int dispatched = 0;
        for (int i = 0; i < jobs.Count && dispatched < MaxDispatchesPerScan; i++)
        {
            var job = jobs[i];
            CrashLog.Log($"NetWatch.Dispatch: attempting '{job.Label}'");

            try
            {
                // re-check that a technician is still free (previous dispatch may have consumed one)
                if (!HasFreeTechnician(techManager))
                {
                    CrashLog.Log("NetWatch.Dispatch: no more free technicians, stopping");
                    break;
                }

                // re-check that the device hasn't been assigned in the meantime
                bool alreadyAssigned = false;
                try
                {
                    alreadyAssigned = techManager.IsDeviceAlreadyAssigned(job.Switch, job.Server);
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"NetWatch.Dispatch.IsDeviceAlreadyAssigned({job.Label})", ex);
                    continue;
                }

                if (alreadyAssigned)
                {
                    CrashLog.Log($"NetWatch.Dispatch: '{job.Label}' already assigned, skipping");
                    continue;
                }

                // dispatch!
                CrashLog.Log($"NetWatch.Dispatch: calling SendTechnician for '{job.Label}'");
                techManager.SendTechnician(job.Switch, job.Server);
                CrashLog.Log($"NetWatch.Dispatch: SendTechnician returned OK for '{job.Label}'");

                dispatched++;

                if (job.Reason == REASON_BROKEN)
                    BrokenRepairs++;
                else
                    EolReplacements++;
                TotalDispatches++;

                _logger?.Msg($"[NetWatch] Dispatched technician: {job.Label}");

                // fire event to rust mods
                try
                {
                    EventDispatcher.FireNetWatchDispatched(job.DeviceType, job.Reason);
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"NetWatch.Dispatch.FireEvent({job.Label})", ex);
                    // non-fatal, dispatch already happened
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException($"NetWatch.Dispatch({job.Label})", ex);
                // stop trying further dispatches this cycle if one crashed
                break;
            }
        }

        CrashLog.Log($"NetWatch.Scan: cycle complete, dispatched {dispatched} technician(s)");
    }

    private static bool HasFreeTechnician(TechnicianManager techManager)
    {
        try
        {
            var techs = techManager.technicians;
            if (techs == null) return false;

            // iterate by index for safer Il2Cpp list access
            int count;
            try { count = techs.Count; }
            catch
            {
                CrashLog.Log("NetWatch.HasFreeTechnician: failed to get technicians.Count");
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var tech = techs[i];
                    if (tech != null && !tech.isBusy)
                        return true;
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"NetWatch.HasFreeTechnician[{i}]", ex);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.HasFreeTechnician", ex);
            return false;
        }
    }

    // ── Collectors: read-only, never mutate game state ──────────────────────

    private static void CollectBrokenServers(NetworkMap networkMap, TechnicianManager techManager, List<DispatchJob> jobs)
    {
        CrashLog.Log("NetWatch.CollectBrokenServers: start");
        try
        {
            var dict = networkMap.brokenServers;
            if (dict == null)
            {
                CrashLog.Log("NetWatch.CollectBrokenServers: dict is null");
                return;
            }

            int count;
            try { count = dict.Count; }
            catch
            {
                CrashLog.Log("NetWatch.CollectBrokenServers: failed to get dict.Count");
                return;
            }

            if (count == 0) return;
            CrashLog.Log($"NetWatch.CollectBrokenServers: {count} broken server(s)");

            // copy keys to a plain array to avoid iterating the Il2Cpp dict directly
            var keys = new List<string>();
            try
            {
                foreach (var kvp in dict)
                {
                    keys.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("NetWatch.CollectBrokenServers.CopyKeys", ex);
                return;
            }

            foreach (var key in keys)
            {
                try
                {
                    Server server;
                    try { server = dict[key]; }
                    catch { continue; } // key may have been removed between copy and access

                    if (server == null) continue;

                    bool assigned = false;
                    try { assigned = techManager.IsDeviceAlreadyAssigned(null, server); }
                    catch (Exception ex)
                    {
                        CrashLog.LogException("NetWatch.CollectBrokenServers.IsAssigned", ex);
                        continue;
                    }

                    if (assigned) continue;

                    jobs.Add(new DispatchJob
                    {
                        Switch = null,
                        Server = server,
                        DeviceType = DEVICE_SERVER,
                        Reason = REASON_BROKEN,
                        Label = "broken server (repair)"
                    });
                }
                catch (Exception ex)
                {
                    CrashLog.LogException("NetWatch.CollectBrokenServers.entry", ex);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.CollectBrokenServers", ex);
        }
    }

    private static void CollectBrokenSwitches(NetworkMap networkMap, TechnicianManager techManager, List<DispatchJob> jobs)
    {
        CrashLog.Log("NetWatch.CollectBrokenSwitches: start");
        try
        {
            var dict = networkMap.brokenSwitches;
            if (dict == null)
            {
                CrashLog.Log("NetWatch.CollectBrokenSwitches: dict is null");
                return;
            }

            int count;
            try { count = dict.Count; }
            catch
            {
                CrashLog.Log("NetWatch.CollectBrokenSwitches: failed to get dict.Count");
                return;
            }

            if (count == 0) return;
            CrashLog.Log($"NetWatch.CollectBrokenSwitches: {count} broken switch(es)");

            // copy keys to avoid Il2Cpp dict iteration issues
            var keys = new List<string>();
            try
            {
                foreach (var kvp in dict)
                {
                    keys.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("NetWatch.CollectBrokenSwitches.CopyKeys", ex);
                return;
            }

            foreach (var key in keys)
            {
                try
                {
                    NetworkSwitch sw;
                    try { sw = dict[key]; }
                    catch { continue; }

                    if (sw == null) continue;

                    bool assigned = false;
                    try { assigned = techManager.IsDeviceAlreadyAssigned(sw, null); }
                    catch (Exception ex)
                    {
                        CrashLog.LogException("NetWatch.CollectBrokenSwitches.IsAssigned", ex);
                        continue;
                    }

                    if (assigned) continue;

                    jobs.Add(new DispatchJob
                    {
                        Switch = sw,
                        Server = null,
                        DeviceType = DEVICE_SWITCH,
                        Reason = REASON_BROKEN,
                        Label = "broken switch (repair)"
                    });
                }
                catch (Exception ex)
                {
                    CrashLog.LogException("NetWatch.CollectBrokenSwitches.entry", ex);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.CollectBrokenSwitches", ex);
        }
    }

    private static void CollectEolServers(NetworkMap networkMap, TechnicianManager techManager, List<DispatchJob> jobs)
    {
        CrashLog.Log("NetWatch.CollectEolServers: start");
        try
        {
            var dict = networkMap.servers;
            if (dict == null)
            {
                CrashLog.Log("NetWatch.CollectEolServers: dict is null");
                return;
            }

            int count;
            try { count = dict.Count; }
            catch
            {
                CrashLog.Log("NetWatch.CollectEolServers: failed to get dict.Count");
                return;
            }

            if (count == 0) return;
            CrashLog.Log($"NetWatch.CollectEolServers: scanning {count} server(s)");

            // copy keys first
            var keys = new List<string>();
            try
            {
                foreach (var kvp in dict)
                {
                    keys.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("NetWatch.CollectEolServers.CopyKeys", ex);
                return;
            }

            int diagNull = 0, diagNotEol = 0, diagBroken = 0, diagAssigned = 0, diagReadFail = 0, diagAdded = 0;

            foreach (var key in keys)
            {
                try
                {
                    Server server;
                    try { server = dict[key]; }
                    catch { diagNull++; continue; }

                    if (server == null) { diagNull++; continue; }

                    // Server uses eolTime (day number) instead of existingWarningSigns
                    int eolTime = 0;
                    bool isBroken = false;
                    try
                    {
                        eolTime = server.eolTime;
                        isBroken = server.isBroken;
                    }
                    catch (Exception ex)
                    {
                        CrashLog.LogException("NetWatch.CollectEolServers.ReadFields", ex);
                        diagReadFail++;
                        continue;
                    }

                    if (isBroken) { diagBroken++; continue; }
                    // eolTime counts down to 0, then goes negative (-1, -2, ...)
                    // server is at/past EOL when eolTime <= 0
                    if (eolTime > 0) { diagNotEol++; continue; }

                    bool assigned = false;
                    try { assigned = techManager.IsDeviceAlreadyAssigned(null, server); }
                    catch (Exception ex)
                    {
                        CrashLog.LogException("NetWatch.CollectEolServers.IsAssigned", ex);
                        continue;
                    }

                    if (assigned) { diagAssigned++; continue; }

                    diagAdded++;
                    jobs.Add(new DispatchJob
                    {
                        Switch = null,
                        Server = server,
                        DeviceType = DEVICE_SERVER,
                        Reason = REASON_EOL,
                        Label = "server EOL warning (replacement)"
                    });
                }
                catch (Exception ex)
                {
                    CrashLog.LogException("NetWatch.CollectEolServers.entry", ex);
                }
            }

            CrashLog.Log($"NetWatch.CollectEolServers: diag — null={diagNull} notEol={diagNotEol} broken={diagBroken} assigned={diagAssigned} readFail={diagReadFail} added={diagAdded}");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.CollectEolServers", ex);
        }
    }

    private static void CollectEolSwitches(NetworkMap networkMap, TechnicianManager techManager, List<DispatchJob> jobs)
    {
        CrashLog.Log("NetWatch.CollectEolSwitches: start");
        try
        {
            var dict = networkMap.switches;
            if (dict == null)
            {
                CrashLog.Log("NetWatch.CollectEolSwitches: dict is null");
                return;
            }

            int count;
            try { count = dict.Count; }
            catch
            {
                CrashLog.Log("NetWatch.CollectEolSwitches: failed to get dict.Count");
                return;
            }

            if (count == 0) return;
            CrashLog.Log($"NetWatch.CollectEolSwitches: scanning {count} switch(es)");

            // copy keys first
            var keys = new List<string>();
            try
            {
                foreach (var kvp in dict)
                {
                    keys.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("NetWatch.CollectEolSwitches.CopyKeys", ex);
                return;
            }

            foreach (var key in keys)
            {
                try
                {
                    NetworkSwitch sw;
                    try { sw = dict[key]; }
                    catch { continue; }

                    if (sw == null) continue;

                    int warningSigns = 0;
                    bool isBroken = false;
                    try
                    {
                        warningSigns = sw.existingWarningSigns;
                        isBroken = sw.isBroken;
                    }
                    catch (Exception ex)
                    {
                        CrashLog.LogException("NetWatch.CollectEolSwitches.ReadFields", ex);
                        continue;
                    }

                    if (warningSigns <= 0 || isBroken) continue;

                    bool assigned = false;
                    try { assigned = techManager.IsDeviceAlreadyAssigned(sw, null); }
                    catch (Exception ex)
                    {
                        CrashLog.LogException("NetWatch.CollectEolSwitches.IsAssigned", ex);
                        continue;
                    }

                    if (assigned) continue;

                    jobs.Add(new DispatchJob
                    {
                        Switch = sw,
                        Server = null,
                        DeviceType = DEVICE_SWITCH,
                        Reason = REASON_EOL,
                        Label = "switch EOL warning (replacement)"
                    });
                }
                catch (Exception ex)
                {
                    CrashLog.LogException("NetWatch.CollectEolSwitches.entry", ex);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("NetWatch.CollectEolSwitches", ex);
        }
    }
}
