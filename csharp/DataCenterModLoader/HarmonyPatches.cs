using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
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
            string saveDataId = "";
            byte objectType = 0;
            int rackUid = -1;

            try { instanceId = __instance?.ServerID ?? ""; } catch { }
            try { saveDataId = __0?.serverID ?? ""; } catch { }
            try { objectType = (byte)(__instance?.serverType ?? 0); } catch { }
            try { rackUid = __0?.rackPositionUID ?? -1; } catch { }

            string serverId = !string.IsNullOrEmpty(instanceId) ? instanceId : saveDataId;

            CrashLog.Log($"ServerInsertedInRack [diag]: instanceId={instanceId}, saveDataId={saveDataId}, resolved={serverId}, type={objectType}, rackUid={rackUid}");

            // When the game assigns a ServerID to a newly placed server,
            // check if MarkPositionAsUsed stored a pending install for it.
            // If so, fire ObjectSpawned + ServerInstalled now that we have the ID.
            if (!string.IsNullOrEmpty(instanceId) && string.IsNullOrEmpty(saveDataId))
            {
                int instId = 0;
                try { instId = __instance.GetInstanceID(); } catch { }

                if (instId != 0 && Patch_Rack_MarkPositionAsUsed.PendingInstalls.Remove(instId, out var pending))
                {
                    int prefabId = FindServerPrefabIndex(__instance);
                    var pos = __instance.transform.position;
                    var rot = __instance.transform.rotation;

                    CrashLog.Log($"[WorldSync] ServerInsertedInRack: deferred sync for '{instanceId}' at uid={pending.rackPosUid} prefab={prefabId}");
                    EventDispatcher.FireObjectSpawned(instanceId, pending.objectType, prefabId, pos, rot);
                    EventDispatcher.FireServerInstalled(instanceId, pending.objectType, pending.rackPosUid);
                }
            }
        }
        catch (Exception ex) { EventDispatcher.LogError($"ServerInsertedInRack: {ex.Message}"); }
    }

    private static int FindServerPrefabIndex(Server srv)
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

/// <summary>
/// PRIMARY hook for "server installed in rack" during gameplay.
/// Fires ServerInstalled immediately when ServerID is known.
/// When ServerID is empty (newly purchased), stores a pending entry
/// that ServerInsertedInRack picks up once the game assigns the ID.
/// </summary>
[HarmonyPatch(typeof(Rack), nameof(Rack.MarkPositionAsUsed))]
internal static class Patch_Rack_MarkPositionAsUsed
{
    internal static bool SuppressEvents = false;

    // Pending installs: Unity instanceId → (rackPosUid, objectType).
    // Filled here when ServerID is empty, consumed by ServerInsertedInRack.
    internal static readonly Dictionary<int, (int rackPosUid, byte objectType)> PendingInstalls = new();

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

            // Find which server was just installed at this rack position.
            var allServers = UnityEngine.Object.FindObjectsOfType<Server>();
            Server installed = null;

            foreach (var srv in allServers)
            {
                try
                {
                    if (srv.currentRackPosition != null &&
                        srv.currentRackPosition.rackPosGlobalUID == rackPosUid)
                    { installed = srv; break; }
                }
                catch { }
            }

            if (installed == null)
            {
                foreach (var srv in allServers)
                {
                    try { if (srv.rackPositionUID == rackPosUid) { installed = srv; break; } }
                    catch { }
                }
            }

            if (installed == null)
            {
                CrashLog.Log($"[WorldSync] MarkPositionAsUsed: index={index} uid={rackPosUid} — could not find installed server among {allServers.Count} servers");
                return;
            }

            string serverId = installed.ServerID ?? "";
            byte objectType = (byte)installed.serverType;

            if (string.IsNullOrEmpty(serverId))
            {
                // Game assigns ServerID later in ServerInsertedInRack (~2s).
                // Store pending entry so that hook can fire the events.
                int instId = installed.GetInstanceID();
                PendingInstalls[instId] = (rackPosUid, objectType);
                CrashLog.Log($"[WorldSync] MarkPositionAsUsed: index={index} uid={rackPosUid} instId={instId} — empty ServerID, stored pending");
                return;
            }

            if (rackPosUid < 0) return;

            CrashLog.Log($"[WorldSync] MarkPositionAsUsed: server '{serverId}' type={objectType} at rackUid={rackPosUid} (index={index}, sizeInU={sizeInU}) → firing event");
            EventDispatcher.FireServerInstalled(serverId, objectType, rackPosUid);
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
    internal static void Postfix(RackPosition __instance)
    {
        try
        {
            CrashLog.Log($"[WorldSync] RackPosition.InteractOnClick: posIndex={__instance.positionIndex} rackPosGlobalUID={__instance.rackPosGlobalUID}");
        }
        catch (Exception ex) { EventDispatcher.LogError($"RackPosition.InteractOnClick: {ex.Message}"); }
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
}

[HarmonyPatch(typeof(HRSystem), nameof(HRSystem.ButtonCancelBuying))]
internal static class Patch_HRSystem_ButtonCancelBuying
{
    internal static void Postfix()
    {
        try { CustomEmployeeManager.ClearPending(); }
        catch (Exception ex) { CrashLog.LogException("ButtonCancelBuying clear pending", ex); }
    }
}

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

[HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonClear))]
internal static class Patch_ComputerShop_ButtonClear
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireShopCartCleared(); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonClear: {ex.Message}"); }
    }
}

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

[HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.RemoveSpawnedItem))]
internal static class Patch_ComputerShop_RemoveSpawnedItem
{
    internal static void Postfix(int __0)
    {
        try { EventDispatcher.FireShopItemRemoved(__0); }
        catch (Exception ex) { EventDispatcher.LogError($"RemoveSpawnedItem: {ex.Message}"); }
    }
}

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
