using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace DataCenterModLoader;

// harmony patches -> rust events

[HarmonyPatch(typeof(Player), nameof(Player.UpdateCoin))]
internal static class Patch_Player_UpdateCoin
{
    private static float _oldMoney;

    internal static void Prefix(Player __instance)
    {
        try { _oldMoney = __instance.money; }
        catch { _oldMoney = 0f; }
    }

    internal static void Postfix(Player __instance)
    {
        try
        {
            float newMoney = __instance.money;
            if (Math.Abs(newMoney - _oldMoney) > 0.001f)
                EventDispatcher.FireValueChanged(EventIds.MoneyChanged, _oldMoney, newMoney, newMoney - _oldMoney);
        }
        catch (Exception ex) { EventDispatcher.LogError($"UpdateCoin postfix: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateXP))]
internal static class Patch_Player_UpdateXP
{
    private static float _oldXP;

    internal static void Prefix(Player __instance)
    {
        try { _oldXP = __instance.xp; }
        catch { _oldXP = 0f; }
    }

    internal static void Postfix(Player __instance)
    {
        try
        {
            float newXP = __instance.xp;
            if (Math.Abs(newXP - _oldXP) > 0.001f)
                EventDispatcher.FireValueChanged(EventIds.XPChanged, _oldXP, newXP, newXP - _oldXP);
        }
        catch (Exception ex) { EventDispatcher.LogError($"UpdateXP postfix: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateReputation))]
internal static class Patch_Player_UpdateReputation
{
    private static float _oldRep;

    internal static void Prefix(Player __instance)
    {
        try { _oldRep = __instance.reputation; }
        catch { _oldRep = 0f; }
    }

    internal static void Postfix(Player __instance)
    {
        try
        {
            float newRep = __instance.reputation;
            if (Math.Abs(newRep - _oldRep) > 0.001f)
                EventDispatcher.FireValueChanged(EventIds.ReputationChanged, _oldRep, newRep, newRep - _oldRep);
        }
        catch (Exception ex) { EventDispatcher.LogError($"UpdateReputation postfix: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.PowerButton))]
internal static class Patch_Server_PowerButton
{
    internal static void Postfix(Server __instance)
    {
        try { EventDispatcher.FireServerPowered(__instance.isOn); }
        catch (Exception ex) { EventDispatcher.LogError($"PowerButton: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.ItIsBroken))]
internal static class Patch_Server_ItIsBroken
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.ServerBroken); }
        catch (Exception ex) { EventDispatcher.LogError($"ItIsBroken: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.RepairDevice))]
internal static class Patch_Server_RepairDevice
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.ServerRepaired); }
        catch (Exception ex) { EventDispatcher.LogError($"RepairDevice: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.ServerInsertedInRack))]
internal static class Patch_Server_ServerInsertedInRack
{
    internal static void Postfix(Server __instance, ServerSaveData __0)
    {
        try
        {
            string instanceId = "";
            byte objectType = 0;
            try { instanceId = __instance?.ServerID ?? ""; } catch { }
            try { objectType = (byte)(__instance?.serverType ?? 0); } catch { }

            // Determine rack position UID from multiple sources
            int rackPosUid = -1;
            try { rackPosUid = __instance?.currentRackPosition?.rackPosGlobalUID ?? -1; } catch { }
            if (rackPosUid <= 0) try { rackPosUid = __instance?.rackPositionUID ?? -1; } catch { }
            if (rackPosUid <= 0) try { rackPosUid = __0?.rackPositionUID ?? -1; } catch { }

            CrashLog.Log($"ServerInsertedInRack [diag]: instanceId={instanceId}, type={objectType}, rackUid={rackPosUid}");

            // Try 1: Consume from PendingRestores dictionary
            var pending = Patch_Rack_MarkPositionAsUsed.ConsumePendingRestore(rackPosUid, objectType);
            if (pending.HasValue)
            {
                if (!string.IsNullOrEmpty(pending.Value.objectId) && instanceId != pending.Value.objectId)
                {
                    try { __instance.ServerID = pending.Value.objectId; } catch { }
                    try { __instance.rackPositionUID = rackPosUid; } catch { }
                    CrashLog.Log($"[WorldSync] ServerInsertedInRack: restored clone ID '{instanceId}' → '{pending.Value.objectId}' rackUid={rackPosUid}");
                }
                return;
            }

            // Try 2: Fallback to InstalledObjects (game re-triggered InsertedInRack internally)
            var installed = Patch_Rack_MarkPositionAsUsed.LookupInstalledObject(rackPosUid, objectType);
            if (installed.HasValue)
            {
                if (!string.IsNullOrEmpty(installed.Value.objectId) && instanceId != installed.Value.objectId)
                {
                    try { __instance.ServerID = installed.Value.objectId; } catch { }
                    try { __instance.rackPositionUID = rackPosUid; } catch { }
                    CrashLog.Log($"[WorldSync] ServerInsertedInRack: restored clone ID (dict fallback) '{instanceId}' → '{installed.Value.objectId}' rackUid={rackPosUid}");
                }
            }
        }
        catch (Exception ex) { EventDispatcher.LogError($"ServerInsertedInRack: {ex.Message}"); }
    }

    internal static int FindServerPrefabIndex(Server srv)
    {
        try
        {
            var mgr = MainGameManager.instance;
            if (mgr?.serverPrefabs == null) return 0;
            string srvName = srv.gameObject?.name ?? "";
            if (srvName.EndsWith("(Clone)")) srvName = srvName.Substring(0, srvName.Length - 7);
            int idx = srvName.LastIndexOf('_');
            string prefix = idx > 0 ? srvName.Substring(0, idx) : srvName;
            for (int i = 0; i < mgr.serverPrefabs.Count; i++)
            {
                try { if (mgr.serverPrefabs[i]?.name == prefix) return i; } catch { }
            }
        }
        catch { }
        return 0;
    }
}

[HarmonyPatch(typeof(NetworkSwitch), nameof(NetworkSwitch.SwitchInsertedInRack))]
internal static class Patch_NetworkSwitch_SwitchInsertedInRack
{
    internal static void Postfix(NetworkSwitch __instance, SwitchSaveData __0)
    {
        try
        {
            string currentId = __instance?.switchId ?? "";

            int rackPosUid = -1;
            try { rackPosUid = __instance?.currentRackPosition?.rackPosGlobalUID ?? -1; } catch { }
            if (rackPosUid <= 0) try { rackPosUid = __instance?.rackPositionUID ?? -1; } catch { }
            if (rackPosUid <= 0) try { rackPosUid = __0?.rackPositionUID ?? -1; } catch { }

            CrashLog.Log($"SwitchInsertedInRack [diag]: switchId={currentId}, rackUid={rackPosUid}");

            var pending = Patch_Rack_MarkPositionAsUsed.ConsumePendingRestore(rackPosUid, 4);
            if (pending.HasValue)
            {
                if (!string.IsNullOrEmpty(pending.Value.objectId) && currentId != pending.Value.objectId)
                {
                    try { __instance.switchId = pending.Value.objectId; } catch { }
                    try { __instance.rackPositionUID = rackPosUid; } catch { }
                    CrashLog.Log($"[WorldSync] SwitchInsertedInRack: restored clone ID '{currentId}' → '{pending.Value.objectId}' rackUid={rackPosUid}");
                }
                return;
            }

            var installed = Patch_Rack_MarkPositionAsUsed.LookupInstalledObject(rackPosUid, 4);
            if (installed.HasValue)
            {
                if (!string.IsNullOrEmpty(installed.Value.objectId) && currentId != installed.Value.objectId)
                {
                    try { __instance.switchId = installed.Value.objectId; } catch { }
                    try { __instance.rackPositionUID = rackPosUid; } catch { }
                    CrashLog.Log($"[WorldSync] SwitchInsertedInRack: restored clone ID (dict fallback) '{currentId}' → '{installed.Value.objectId}' rackUid={rackPosUid}");
                }
            }
        }
        catch (Exception ex) { EventDispatcher.LogError($"SwitchInsertedInRack: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(PatchPanel), nameof(PatchPanel.InsertedInRack))]
internal static class Patch_PatchPanel_InsertedInRack
{
    internal static void Postfix(PatchPanel __instance, PatchPanelSaveData __0)
    {
        try
        {
            string currentId = __instance?.patchPanelId ?? "";

            int rackPosUid = -1;
            try { rackPosUid = __instance?.currentRackPosition?.rackPosGlobalUID ?? -1; } catch { }
            if (rackPosUid <= 0) try { rackPosUid = __instance?.rackPositionUID ?? -1; } catch { }
            if (rackPosUid <= 0) try { rackPosUid = __0?.rackPositionUID ?? -1; } catch { }

            CrashLog.Log($"PatchPanel.InsertedInRack [diag]: patchPanelId={currentId}, rackUid={rackPosUid}");

            var pending = Patch_Rack_MarkPositionAsUsed.ConsumePendingRestore(rackPosUid, 7);
            if (pending.HasValue)
            {
                if (!string.IsNullOrEmpty(pending.Value.objectId) && currentId != pending.Value.objectId)
                {
                    try { __instance.patchPanelId = pending.Value.objectId; } catch { }
                    try { __instance.rackPositionUID = rackPosUid; } catch { }
                    CrashLog.Log($"[WorldSync] PatchPanel.InsertedInRack: restored clone ID '{currentId}' → '{pending.Value.objectId}' rackUid={rackPosUid}");
                }
                return;
            }

            var installed = Patch_Rack_MarkPositionAsUsed.LookupInstalledObject(rackPosUid, 7);
            if (installed.HasValue)
            {
                if (!string.IsNullOrEmpty(installed.Value.objectId) && currentId != installed.Value.objectId)
                {
                    try { __instance.patchPanelId = installed.Value.objectId; } catch { }
                    try { __instance.rackPositionUID = rackPosUid; } catch { }
                    CrashLog.Log($"[WorldSync] PatchPanel.InsertedInRack: restored clone ID (dict fallback) '{currentId}' → '{installed.Value.objectId}' rackUid={rackPosUid}");
                }
            }
        }
        catch (Exception ex) { EventDispatcher.LogError($"PatchPanel.InsertedInRack: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Rack), nameof(Rack.MarkPositionAsUsed))]
internal static class Patch_Rack_MarkPositionAsUsed
{
    internal static bool SuppressEvents = false;

    // Dictionary-based pending restores keyed by rackPosGlobalUID.
    // Each install gets its own slot — concurrent remote installs can no longer
    // overwrite the pending entry for a local install.
    internal static Dictionary<int, (string objectId, byte objectType)> PendingRestores = new();

    // Permanent map: rackPosUid → stableId.
    // Used as fallback when the game internally re-triggers InsertedInRack
    // (e.g. rack bookkeeping, save callbacks) after PendingRestores was consumed.
    internal static Dictionary<int, (string objectId, byte objectType)> InstalledObjects = new();

    // Backward-compat property — GameAPI.cs still sets PendingCloneRestore = (...).
    // The setter migrates the value into both dictionaries automatically.
    internal static (string objectId, byte objectType, int rackPosUid)? PendingCloneRestore
    {
        get => null;
        set
        {
            if (value.HasValue && value.Value.rackPosUid > 0)
            {
                PendingRestores[value.Value.rackPosUid] = (value.Value.objectId, value.Value.objectType);
                InstalledObjects[value.Value.rackPosUid] = (value.Value.objectId, value.Value.objectType);
            }
        }
    }

    // Dedup guard — Il2Cpp fires every Harmony postfix twice for the same call.
    private static int _lastFiredUid = -1;
    private static long _lastFiredTick = 0;

    /// <summary>
    /// Consume and remove the pending restore entry for the given rack position.
    /// Returns null if no matching entry exists.
    /// </summary>
    internal static (string objectId, byte objectType)? ConsumePendingRestore(int rackPosUid, byte expectedType)
    {
        if (rackPosUid > 0 && PendingRestores.TryGetValue(rackPosUid, out var entry))
        {
            bool typeMatch = expectedType switch
            {
                <= 3 => entry.objectType <= 3,
                4 => entry.objectType == 4,
                7 => entry.objectType == 7,
                _ => false
            };
            if (typeMatch)
            {
                PendingRestores.Remove(rackPosUid);
                return entry;
            }
        }
        return null;
    }

    /// <summary>
    /// Look up the stable ID for the object installed at a rack position.
    /// Does NOT remove the entry — this is a persistent fallback.
    /// </summary>
    internal static (string objectId, byte objectType)? LookupInstalledObject(int rackPosUid, byte expectedType)
    {
        if (rackPosUid > 0 && InstalledObjects.TryGetValue(rackPosUid, out var entry))
        {
            bool typeMatch = expectedType switch
            {
                <= 3 => entry.objectType <= 3,
                4 => entry.objectType == 4,
                7 => entry.objectType == 7,
                _ => false
            };
            if (typeMatch) return entry;
        }
        return null;
    }

    /// <summary>
    /// Remove an entry from both dictionaries. Call on uninstall.
    /// </summary>
    internal static void RemoveInstalledObject(int rackPosUid)
    {
        InstalledObjects.Remove(rackPosUid);
        PendingRestores.Remove(rackPosUid);
    }

    internal static void Postfix(Rack __instance, int __0, int __1)
    {
        try
        {
            if (SuppressEvents) return;
            int index = __0;
            int sizeInU = __1;

            var positions = __instance.positions;
            if (positions == null || index < 0 || index >= positions.Count) return;

            RackPosition rackPos = positions[index];
            if (rackPos == null) return;

            int rackPosUid = rackPos.rackPosGlobalUID;

            string objectId = null;
            byte objectType = 0;

            // ── First, try to use the known held object (most reliable) ──
            string heldId = Patch_UsableObject_InteractOnClick.GetHeldObjectId();
            byte heldType = Patch_UsableObject_InteractOnClick.GetHeldObjectType();
            if (!string.IsNullOrEmpty(heldId))
            {
                objectId = heldId;
                objectType = heldType;
            }

            // ── Fallback: scan scene objects (legacy, less reliable) ──
            if (objectId == null)
            {
                var allServers = UnityEngine.Object.FindObjectsOfType<Server>();
                foreach (var srv in allServers)
                {
                    try
                    {
                        if ((srv.currentRackPosition != null && srv.currentRackPosition.rackPosGlobalUID == rackPosUid)
                            || srv.rackPositionUID == rackPosUid)
                        {
                            objectId = srv.ServerID ?? "";
                            objectType = (byte)srv.serverType;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (objectId == null)
            {
                foreach (var sw in UnityEngine.Object.FindObjectsOfType<NetworkSwitch>())
                {
                    try
                    {
                        if ((sw.currentRackPosition != null && sw.currentRackPosition.rackPosGlobalUID == rackPosUid)
                            || sw.rackPositionUID == rackPosUid)
                        {
                            objectId = sw.switchId ?? "";
                            objectType = 4;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (objectId == null)
            {
                foreach (var pp in UnityEngine.Object.FindObjectsOfType<PatchPanel>())
                {
                    try
                    {
                        if ((pp.currentRackPosition != null && pp.currentRackPosition.rackPosGlobalUID == rackPosUid)
                            || pp.rackPositionUID == rackPosUid)
                        {
                            objectId = pp.patchPanelId ?? "";
                            objectType = 7;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (string.IsNullOrEmpty(objectId))
            {
                CrashLog.Log($"[WorldSync] MarkPositionAsUsed: index={index} uid={rackPosUid} — could not identify installed object");
                return;
            }

            if (rackPosUid < 0) return;

            // Dedup guard — Il2Cpp fires the postfix twice per call
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (rackPosUid == _lastFiredUid && (now - _lastFiredTick) < 200)
                return;
            _lastFiredUid = rackPosUid;
            _lastFiredTick = now;

            PendingRestores[rackPosUid] = (objectId, objectType);
            InstalledObjects[rackPosUid] = (objectId, objectType);

            CrashLog.Log($"[WorldSync] MarkPositionAsUsed: '{objectId}' type={objectType} at rackUid={rackPosUid} (index={index}, sizeInU={sizeInU}) → firing event");
            EventDispatcher.FireServerInstalled(objectId, objectType, rackPosUid);
            CarryStateMonitor.SuppressNextDrop();
            Patch_UsableObject_InteractOnClick.ClearHeldObject();
        }
        catch (Exception ex) { EventDispatcher.LogError($"MarkPositionAsUsed: {ex.Message}"); }
    }
}

/// <summary>
/// Diagnostic hook for RackPosition.InteractOnClick — logs when a player
/// clicks on a rack slot (start of the installation coroutine).
/// </summary>
[HarmonyPatch(typeof(RackPosition), nameof(RackPosition.InteractOnClick))]
internal static class Patch_RackPosition_InteractOnClick
{
    [ThreadStatic] private static int _prevNumObjects;
    [ThreadStatic] private static int _prevObjectInHand;

    internal static void Prefix(RackPosition __instance)
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null) return;
            _prevNumObjects = pm.numberOfObjectsInHand;
            _prevObjectInHand = (int)pm.objectInHand;
        }
        catch { }
    }

    internal static void Postfix(RackPosition __instance)
    {
        try
        {
            CrashLog.Log($"[WorldSync] RackPosition.InteractOnClick: posIndex={__instance.positionIndex} rackPosGlobalUID={__instance.rackPosGlobalUID}");
        }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] RackPosition.InteractOnClick Postfix error: {ex.Message}"); }
    }
}

// NetWatchSystem also uses this to detect day changes for salary deduction
[HarmonyPatch(typeof(TimeController), "Update")]
internal static class Patch_TimeController_Update
{
    private static int _lastDay = -1;

    internal static void Postfix(TimeController __instance)
    {
        try
        {
            int currentDay = __instance.day;
            if (_lastDay >= 0 && currentDay != _lastDay)
                EventDispatcher.FireDayEnded((uint)currentDay);
            _lastDay = currentDay;
        }
        catch (Exception ex) { EventDispatcher.LogError($"TimeController.Update: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(MainGameManager), nameof(MainGameManager.ButtonCustomerChosen))]
internal static class Patch_MainGameManager_ButtonCustomerChosen
{
    internal static void Postfix(int __0)
    {
        try { EventDispatcher.FireCustomerAccepted(__0); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonCustomerChosen: {ex.Message}"); }
    }
}
[HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonCheckOut))]
internal static class Patch_ComputerShop_ButtonCheckOut
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.ShopCheckout); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonCheckOut: {ex.Message}"); }

        // After checkout all items are already spawned in the scene.
        // Detect them here instead of patching SpawnPhysicalItem (which causes
        // IL2CPP colour/material corruption when Harmony touches it at all).
        try { SpawnedObjectTracker.DetectNewObjects(); }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] ButtonCheckOut scan error: {ex.Message}"); }
    }
}

internal static class SpawnedObjectTracker
{
    internal static bool SuppressEvents = false;

    private static readonly HashSet<int> _knownInstances = new();
    private static readonly HashSet<string> _knownIds = new();

    internal static void DetectNewObjects()
    {
        if (SuppressEvents) return;
        try
        {
            foreach (var uo in UnityEngine.Object.FindObjectsOfType<UsableObject>())
            {
                int instId = uo.GetInstanceID();
                if (!_knownInstances.Add(instId)) continue; // already tracked

                ProcessNewObject(uo, instId);
            }
        }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] DetectNewObjects error: {ex.Message}"); }
    }

    private static void ProcessNewObject(UsableObject uo, int instId)
    {
        string objectId = null;
        byte objectType = 0;
        int prefabId = 0;

        var server = uo.TryCast<Server>();
        if (server != null)
        {
            objectId = server.ServerID ?? "";
            if (string.IsNullOrEmpty(objectId))
            {
                string n = server.gameObject?.name ?? "Server";
                if (n.EndsWith("(Clone)")) n = n[..^7];
                objectId = $"{n}_{instId}";
                server.ServerID = objectId;
            }
            objectType = (byte)server.serverType;
            prefabId = Patch_Server_ServerInsertedInRack.FindServerPrefabIndex(server);
        }
        else
        {
            var sw = uo.TryCast<NetworkSwitch>();
            if (sw != null)
            {
                objectId = sw.switchId ?? "";
                if (string.IsNullOrEmpty(objectId))
                {
                    string n = sw.gameObject?.name ?? "Switch";
                    if (n.EndsWith("(Clone)")) n = n[..^7];
                    objectId = Patch_UsableObject_InteractOnClick.GenerateDeterministicId(n, sw.transform.position);
                    sw.switchId = objectId;
                }
                objectType = 4;
                try
                {
                    var mgr = MainGameManager.instance;
                    if (mgr?.switchesPrefabs != null)
                    {
                        string swName = sw.gameObject?.name ?? "";
                        if (swName.EndsWith("(Clone)")) swName = swName[..^7];
                        for (int i = 0; i < mgr.switchesPrefabs.Count; i++)
                        {
                            try { if (mgr.switchesPrefabs[i]?.name == swName) { prefabId = i; break; } } catch { }
                        }
                    }
                }
                catch { }
            }
            else
            {
                var pp = uo.TryCast<PatchPanel>();
                if (pp != null)
                {
                    objectId = pp.patchPanelId ?? "";
                    if (string.IsNullOrEmpty(objectId))
                    {
                        string n = pp.gameObject?.name ?? "PatchPanel";
                        if (n.EndsWith("(Clone)")) n = n[..^7];
                        objectId = Patch_UsableObject_InteractOnClick.GenerateDeterministicId(n, pp.transform.position);
                        pp.patchPanelId = objectId;
                    }
                    objectType = 7;
                    try
                    {
                        var mgr = MainGameManager.instance;
                        if (mgr != null)
                        {
                            var prefabGo = mgr.GetPatchPanelPrefab(pp.patchPanelType);
                            if (prefabGo != null) prefabId = pp.patchPanelType;
                        }
                    }
                    catch { }
                }
                else return; // not a tracked type
            }
        }

        if (string.IsNullOrEmpty(objectId)) return;
        if (!_knownIds.Add(objectId))
        {
            CrashLog.Log($"[WorldSync] DetectNewObjects: skipping '{objectId}' (already known by ID)");
            return;
        }

        var pos = uo.transform.position;
        var rot = uo.transform.rotation;
        CrashLog.Log($"[WorldSync] DetectNewObjects: new object '{objectId}' type={objectType} prefab={prefabId} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})");
        EventDispatcher.FireObjectSpawned(objectId, objectType, prefabId, pos, rot);
    }

    /// <summary>
    /// Populate known instances from the current scene so we don't re-fire ObjectSpawned
    /// for objects that came from the save file. Call this after scene load / save load.
    /// </summary>
    internal static void PopulateKnownServers()
    {
        _knownInstances.Clear();
        _knownIds.Clear();
        try
        {
            foreach (var uo in UnityEngine.Object.FindObjectsOfType<UsableObject>())
            {
                _knownInstances.Add(uo.GetInstanceID());
                string id = "";
                var srv = uo.TryCast<Server>(); if (srv != null) id = srv.ServerID ?? "";
                var sw = uo.TryCast<NetworkSwitch>(); if (sw != null) id = sw.switchId ?? "";
                var pp = uo.TryCast<PatchPanel>(); if (pp != null) id = pp.patchPanelId ?? "";
                if (!string.IsNullOrEmpty(id)) _knownIds.Add(id);
            }
            CrashLog.Log($"[WorldSync] PopulateKnownServers: {_knownInstances.Count} instances, {_knownIds.Count} IDs");
        }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] PopulateKnownServers error: {ex.Message}"); }
    }

    /// <summary>
    /// Register a remotely-spawned object so we don't re-detect it.
    /// Called from WorldSpawnObjectImpl after creating an object.
    /// </summary>
    internal static void RegisterRemoteSpawn(int instanceId, string serverId = null)
    {
        _knownInstances.Add(instanceId);
        if (!string.IsNullOrEmpty(serverId))
            _knownIds.Add(serverId);
    }
}

[HarmonyPatch(typeof(HRSystem), nameof(HRSystem.ButtonConfirmHire))]
internal static class Patch_HRSystem_ButtonConfirmHire
{
    private static bool _wasCustom;

    internal static bool Prefix(HRSystem __instance)
    {
        try
        {
            if (CustomEmployeeManager.HandleConfirmHire(__instance))
            {
                _wasCustom = true;
                return false;
            }
        }
        catch (Exception ex) { CrashLog.LogException("ButtonConfirmHire prefix", ex); }
        _wasCustom = false;
        return true;
    }

    internal static void Postfix()
    {
        if (_wasCustom) return;
        try { EventDispatcher.FireSimple(EventIds.EmployeeHired); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonConfirmHire: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(HRSystem), nameof(HRSystem.ButtonConfirmFireEmployee))]
internal static class Patch_HRSystem_ButtonConfirmFireEmployee
{
    private static bool _wasCustom;

    internal static bool Prefix(HRSystem __instance)
    {
        try
        {
            if (CustomEmployeeManager.HandleConfirmFire(__instance))
            {
                _wasCustom = true;
                return false;
            }
        }
        catch (Exception ex) { CrashLog.LogException("ButtonConfirmFireEmployee prefix", ex); }
        _wasCustom = false;
        return true;
    }

    internal static void Postfix()
    {
        if (_wasCustom) return;
        try { EventDispatcher.FireSimple(EventIds.EmployeeFired); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonConfirmFireEmployee: {ex.Message}"); }
    }
}/*

[HarmonyPatch(typeof(HRSystem), nameof(HRSystem.ButtonCancelBuying))]
internal static class Patch_HRSystem_ButtonCancelBuying
{
    internal static void Postfix()
    {
        try { CustomEmployeeManager.ClearPending(); }
        catch (Exception ex) { CrashLog.LogException("ButtonCancelBuying clear pending", ex); }
    }
}*/

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
internal static class Patch_SaveSystem_SaveGame
{
    internal static void Postfix()
    {
        try
        {
            CustomEmployeeManager.SaveState();
            EventDispatcher.FireSimple(EventIds.GameSaved);
        }
        catch (Exception ex) { EventDispatcher.LogError($"SaveGame: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.Load))]
internal static class Patch_SaveSystem_Load
{
    internal static void Postfix()
    {
        try
        {
            CustomEmployeeManager.LoadState();
            EventDispatcher.FireSimple(EventIds.GameLoaded);
            SpawnedObjectTracker.PopulateKnownServers();
        }
        catch (Exception ex) { EventDispatcher.LogError($"Load: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(CustomerBase), nameof(CustomerBase.AreAllAppRequirementsMet))]
internal static class Patch_CustomerBase_AreAllAppRequirementsMet
{
    private static readonly HashSet<int> _satisfiedCustomers = new();

    internal static void Postfix(CustomerBase __instance, bool __result)
    {
        try
        {
            int id = __instance.customerBaseID;
            if (__result)
            {
                if (_satisfiedCustomers.Add(id))
                    EventDispatcher.FireCustomerSatisfied(id);
            }
            else
            {
                if (_satisfiedCustomers.Remove(id))
                    EventDispatcher.FireCustomerUnsatisfied(id);
            }
        }
        catch (Exception ex) { EventDispatcher.LogError($"AreAllAppRequirementsMet: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.RegisterLink))]
internal static class Patch_Server_RegisterLink
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireCableConnected(); }
        catch (Exception ex) { EventDispatcher.LogError($"RegisterLink: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.UnregisterLink))]
internal static class Patch_Server_UnregisterLink
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireCableDisconnected(); }
        catch (Exception ex) { EventDispatcher.LogError($"UnregisterLink: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.UpdateCustomer))]
internal static class Patch_Server_UpdateCustomer
{
    internal static void Postfix(int newCustomerID)
    {
        try { EventDispatcher.FireServerCustomerChanged(newCustomerID); }
        catch (Exception ex) { EventDispatcher.LogError($"UpdateCustomer: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.UpdateAppID))]
internal static class Patch_Server_UpdateAppID
{
    internal static void Postfix(int _appID)
    {
        try { EventDispatcher.FireServerAppChanged(_appID); }
        catch (Exception ex) { EventDispatcher.LogError($"UpdateAppID: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Rack), nameof(Rack.ButtonUnmountRack))]
internal static class Patch_Rack_ButtonUnmountRack
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireRackUnmounted(); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonUnmountRack: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(NetworkMap), nameof(NetworkMap.AddBrokenSwitch))]
internal static class Patch_NetworkMap_AddBrokenSwitch
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSwitchBroken(); }
        catch (Exception ex) { EventDispatcher.LogError($"AddBrokenSwitch: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(NetworkMap), nameof(NetworkMap.RemoveBrokenSwitch))]
internal static class Patch_NetworkMap_RemoveBrokenSwitch
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSwitchRepaired(); }
        catch (Exception ex) { EventDispatcher.LogError($"RemoveBrokenSwitch: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(BalanceSheet), nameof(BalanceSheet.SaveSnapshot))]
internal static class Patch_BalanceSheet_SaveSnapshot
{
    internal static void Postfix(int __0)
    {
        try { EventDispatcher.FireMonthEnded(__0); }
        catch (Exception ex) { EventDispatcher.LogError($"SaveSnapshot: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonBuyShopItem))]
internal static class Patch_ComputerShop_ButtonBuyShopItem
{
    internal static void Postfix(int __0, int __1, int __2)
    {
        try { EventDispatcher.FireShopItemAdded(__0, __1, __2); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonBuyShopItem: {ex.Message}"); }
    }
}
/*
[HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonClear))]
internal static class Patch_ComputerShop_ButtonClear
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireShopCartCleared(); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonClear: {ex.Message}"); }
    }
}*/

[HarmonyPatch(typeof(MainGameManager), nameof(MainGameManager.ButtonBuyWall))]
internal static class Patch_MainGameManager_ButtonBuyWall
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireWallPurchased(); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonBuyWall: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.AutoSave))]
internal static class Patch_SaveSystem_AutoSave
{
    internal static void Postfix()
    {
        try
        {
            CustomEmployeeManager.SaveState();
            EventDispatcher.FireGameAutoSaved();
        }
        catch (Exception ex) { EventDispatcher.LogError($"AutoSave: {ex.Message}"); }
    }
}
/*
[HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.RemoveSpawnedItem))]
internal static class Patch_ComputerShop_RemoveSpawnedItem
{
    internal static void Postfix(int __0)
    {
        try { EventDispatcher.FireShopItemRemoved(__0); }
        catch (Exception ex) { EventDispatcher.LogError($"RemoveSpawnedItem: {ex.Message}"); }
    }
}
*/
/// <summary>
/// Patches HRSystem.OnEnable to inject custom employee cards into the HR panel.
/// The panel is toggled via SetActive, so OnEnable fires each time it opens.
/// Note: HRSystem does NOT override Start(), so we cannot patch it in Il2Cpp.
/// </summary>
[HarmonyPatch(typeof(HRSystem), "OnEnable")]
internal static class Patch_HRSystem_OnEnable
{
    internal static void Postfix(HRSystem __instance)
    {
        try
        {
            CrashLog.Log("HRSystem.OnEnable: injecting custom employees");
            CustomEmployeeManager.InjectIntoHRSystem(__instance);
        }
        catch (Exception ex)
        {
            CrashLog.LogException("HRSystem.OnEnable custom employee injection", ex);
        }
    }
}

/// <summary>
/// Detects when the local player picks up or drops a UsableObject (Server, Switch, etc.)
/// by comparing PlayerManager state before/after InteractOnClick.
/// Fires ObjectPickedUp / ObjectDropped events for multiplayer synchronization.
/// </summary>
[HarmonyPatch(typeof(UsableObject), nameof(UsableObject.InteractOnClick))]
internal static class Patch_UsableObject_InteractOnClick
{
    internal static bool SuppressEvents = false;

    // Track what the player was holding before interaction
    [ThreadStatic] private static int _prevNumObjects;
    [ThreadStatic] private static int _prevObjectInHand;

    // Track the currently held object for drop detection
    private static string _heldObjectId = null;
    private static byte _heldObjectType = 0;
    private static UsableObject _heldObjectRef = null;

    // Guard against double-fire when the game calls InteractOnClick on both
    // base class and derived class in the same interaction (virtual dispatch).
    private static bool _pickupFiredThisInteraction = false;

    internal static void Prefix(UsableObject __instance)
    {
        _pickupFiredThisInteraction = false;
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null) return;
            _prevNumObjects = pm.numberOfObjectsInHand;
            _prevObjectInHand = (int)pm.objectInHand;
        }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] InteractOnClick Prefix error: {ex.Message}"); }
    }

    internal static void Postfix(UsableObject __instance)
    {
        if (SuppressEvents) return;

        try
        {
            var pm = PlayerManager.instance;
            if (pm == null) return;

            int newNumObjects = pm.numberOfObjectsInHand;
            int newObjectInHand = (int)pm.objectInHand;

            // ── PICKUP: was empty-handed, now holding something ──
            if (_prevNumObjects == 0 && newNumObjects > 0 && _prevObjectInHand == 0 && newObjectInHand != 0)
            {
                string objectId = null;
                byte objectType = 0;

                var server = __instance.TryCast<Server>();
                if (server != null)
                {
                    objectId = server.ServerID ?? "";
                    objectType = (byte)server.serverType;
                }
                else
                {
                    var netSwitch = __instance.TryCast<NetworkSwitch>();
                    if (netSwitch != null)
                    {
                        objectId = netSwitch.switchId ?? "";
                        objectType = (byte)(int)__instance.objectInHandType;
                    }
                    else
                    {
                        var patchPanel = __instance.TryCast<PatchPanel>();
                        if (patchPanel != null)
                        {
                            objectId = patchPanel.patchPanelId ?? "";
                            if (string.IsNullOrEmpty(objectId))
                            {
                                string objName = patchPanel.gameObject?.name ?? "PatchPanel";
                                if (objName.EndsWith("(Clone)"))
                                    objName = objName.Substring(0, objName.Length - 7);
                                objectId = GenerateDeterministicId(objName, patchPanel.transform.position);
                                patchPanel.patchPanelId = objectId;
                                CrashLog.Log($"[WorldSync] InteractOnClick: assigned patchPanelId '{objectId}' (position-based)");
                            }
                            objectType = (byte)(int)__instance.objectInHandType;
                        }
                        else
                        {
                            string objName = __instance.gameObject.name;
                            if (objName.EndsWith("(Clone)")) objName = objName[..^7];
                            var p = __instance.transform.position;
                            int posHash = ((int)(p.x * 100)) ^ ((int)(p.y * 100) << 10) ^ ((int)(p.z * 100) << 20);
                            objectId = $"{objName}_{posHash}";
                            objectType = (byte)(int)__instance.objectInHandType;
                        }
                    }
                }

                // ── Resolve clone IDs to stable IDs using InstalledObjects ──
                // When the game re-clones an object during a rack install and our
                // restore didn't fire (race condition), the object's field contains
                // a clone ID. Look it up from InstalledObjects by rack position UID.
                if (!string.IsNullOrEmpty(objectId))
                {
                    int pickupRackUid = -1;
                    try
                    {
                        switch (objectType)
                        {
                            case 0:
                            case 1:
                            case 2:
                            case 3:
                                var srv2 = __instance.TryCast<Server>();
                                if (srv2 != null)
                                {
                                    if (srv2.currentRackPosition != null)
                                        pickupRackUid = srv2.currentRackPosition.rackPosGlobalUID;
                                    if (pickupRackUid <= 0)
                                        pickupRackUid = srv2.rackPositionUID;
                                }
                                break;
                            case 4:
                                var sw2 = __instance.TryCast<NetworkSwitch>();
                                if (sw2 != null)
                                {
                                    if (sw2.currentRackPosition != null)
                                        pickupRackUid = sw2.currentRackPosition.rackPosGlobalUID;
                                    if (pickupRackUid <= 0)
                                        pickupRackUid = sw2.rackPositionUID;
                                }
                                break;
                            case 7:
                                var pp2 = __instance.TryCast<PatchPanel>();
                                if (pp2 != null)
                                {
                                    if (pp2.currentRackPosition != null)
                                        pickupRackUid = pp2.currentRackPosition.rackPosGlobalUID;
                                    if (pickupRackUid <= 0)
                                        pickupRackUid = pp2.rackPositionUID;
                                }
                                break;
                        }
                    }
                    catch { }

                    if (pickupRackUid > 0)
                    {
                        var stableEntry = Patch_Rack_MarkPositionAsUsed.LookupInstalledObject(pickupRackUid, objectType);
                        if (stableEntry.HasValue && !string.IsNullOrEmpty(stableEntry.Value.objectId) && stableEntry.Value.objectId != objectId)
                        {
                            CrashLog.Log($"[WorldSync] Pickup: resolved clone ID '{objectId}' → stable '{stableEntry.Value.objectId}' (rackUid={pickupRackUid})");
                            objectId = stableEntry.Value.objectId;
                            // Also fix the object's field so the game stays consistent
                            try
                            {
                                switch (objectType)
                                {
                                    case 0:
                                    case 1:
                                    case 2:
                                    case 3:
                                        var srv3 = __instance.TryCast<Server>();
                                        if (srv3 != null) srv3.ServerID = objectId;
                                        break;
                                    case 4:
                                        var sw3 = __instance.TryCast<NetworkSwitch>();
                                        if (sw3 != null) sw3.switchId = objectId;
                                        break;
                                    case 7:
                                        var pp3 = __instance.TryCast<PatchPanel>();
                                        if (pp3 != null) pp3.patchPanelId = objectId;
                                        break;
                                }
                            }
                            catch { }
                        }
                        // Object is leaving the rack — remove from tracking
                        Patch_Rack_MarkPositionAsUsed.RemoveInstalledObject(pickupRackUid);
                    }
                }

                if (!string.IsNullOrEmpty(objectId) && !_pickupFiredThisInteraction)
                {
                    _pickupFiredThisInteraction = true;
                    _heldObjectId = objectId;
                    _heldObjectType = objectType;
                    _heldObjectRef = __instance;

                    CrashLog.Log($"[WorldSync] Pickup tracked: '{objectId}' type={objectType}");
                }
            }
            // ── DROP: was holding something, now empty-handed ──
            else if (_prevNumObjects > 0 && newNumObjects == 0 && _prevObjectInHand != 0 && newObjectInHand == 0)
            {
                if (!string.IsNullOrEmpty(_heldObjectId))
                {

                    CrashLog.Log($"[WorldSync] Drop tracked: '{_heldObjectId}' type={_heldObjectType}");
                    _heldObjectId = null;
                    _heldObjectType = 0;
                    _heldObjectRef = null;
                }
            }
        }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] InteractOnClick Postfix error: {ex.Message}"); }
    }

    /// <summary>
    /// Generates a deterministic object ID based on position instead of Unity instance ID.
    /// This ensures host and client produce the same ID for the same physical object.
    /// </summary>
    internal static string GenerateDeterministicId(string baseName, UnityEngine.Vector3 pos)
    {
        // Round to centimeters to avoid floating-point differences between host/client
        int px = Mathf.RoundToInt(pos.x * 100);
        int py = Mathf.RoundToInt(pos.y * 100);
        int pz = Mathf.RoundToInt(pos.z * 100);
        // Combine into a hash that's unlikely to collide for objects at different positions
        uint hash = (uint)(px * 73856093 ^ py * 19349663 ^ pz * 83492791);
        return $"{baseName}_{hash}";
    }

    /// <summary>
    /// Called from remote world actions to suppress local event firing
    /// </summary>
    internal static void SetHeldObject(string objectId, byte objectType, UsableObject obj)
    {
        _heldObjectId = objectId;
        _heldObjectType = objectType;
        _heldObjectRef = obj;
    }

    internal static void ClearHeldObject()
    {
        _heldObjectId = null;
        _heldObjectType = 0;
        _heldObjectRef = null;
    }

    internal static string GetHeldObjectId() => _heldObjectId;
    internal static byte GetHeldObjectType() => _heldObjectType;
}

/// <summary>
/// Per-frame carry state monitor. Detects drops that don't go through InteractOnClick
/// (e.g. pressing the drop key). Called from Core.OnUpdate().
/// </summary>
internal static class CarryStateMonitor
{
    private static int _prevNumObjects = 0;
    private static int _prevObjectInHand = 0;
    private static bool _initialized = false;

    // Flag set when an InstalledInRack event fires, to suppress false drop detection
    private static bool _suppressNextDrop = false;
    private static long _suppressTick = 0;

    internal static void SuppressNextDrop()
    {
        _suppressNextDrop = true;
        _suppressTick = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    internal static void Update()
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null) return;

            int curNumObjects = pm.numberOfObjectsInHand;
            int curObjectInHand = (int)pm.objectInHand;

            if (!_initialized)
            {
                _prevNumObjects = curNumObjects;
                _prevObjectInHand = curObjectInHand;
                _initialized = true;
                return;
            }

            // Clear suppress flag after 500ms to avoid permanently suppressing
            if (_suppressNextDrop)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if ((now - _suppressTick) > System.Diagnostics.Stopwatch.Frequency / 2)
                    _suppressNextDrop = false;
            }


            if (_prevNumObjects > 0 && curNumObjects == 0 && _prevObjectInHand != 0 && curObjectInHand == 0)
            {
                if (_suppressNextDrop)
                {
                    _suppressNextDrop = false;
                    Patch_UsableObject_InteractOnClick.ClearHeldObject();
                }
            }

            _prevNumObjects = curNumObjects;
            _prevObjectInHand = curObjectInHand;
        }
        catch (Exception ex) { CrashLog.Log($"[WorldSync] CarryStateMonitor error: {ex.Message}"); }
    }

    internal static void Reset()
    {
        _prevNumObjects = 0;
        _prevObjectInHand = 0;
        _initialized = false;
        _suppressNextDrop = false;
    }
}
