using System;
using HarmonyLib;
using Il2Cpp;

namespace DataCenterModLoader;

// Harmony patches that intercept game methods and forward events to Rust mods.

[HarmonyPatch(typeof(Player), nameof(Player.UpdateCoin))]
internal static class Patch_Player_UpdateCoin
{
    private static float _oldMoney;

    internal static void Prefix(Player __instance)
    {
        try { _oldMoney = __instance.money; }
        catch { _oldMoney = 0f; }
    }

    internal static void Postfix(Player __instance, float __0)
    {
        try
        {
            float newMoney = __instance.money;
            if (Math.Abs(newMoney - _oldMoney) > 0.001f)
                EventDispatcher.FireValueChanged(EventIds.MoneyChanged, _oldMoney, newMoney, __0);
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

    internal static void Postfix(Player __instance, float __0)
    {
        try
        {
            float newXP = __instance.xp;
            if (Math.Abs(newXP - _oldXP) > 0.001f)
                EventDispatcher.FireValueChanged(EventIds.XPChanged, _oldXP, newXP, __0);
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

    internal static void Postfix(Player __instance, float __0)
    {
        try
        {
            float newRep = __instance.reputation;
            if (Math.Abs(newRep - _oldRep) > 0.001f)
                EventDispatcher.FireValueChanged(EventIds.ReputationChanged, _oldRep, newRep, __0);
        }
        catch (Exception ex) { EventDispatcher.LogError($"UpdateReputation postfix: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), nameof(Server.PowerButton))]
internal static class Patch_Server_PowerButton
{
    internal static void Postfix(bool __0)
    {
        try { EventDispatcher.FireServerPowered(__0); }
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
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.ServerInstalled); }
        catch (Exception ex) { EventDispatcher.LogError($"ServerInsertedInRack: {ex.Message}"); }
    }
}

// Tracks day changes by watching TimeController.Update each frame
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
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.EmployeeHired); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonConfirmHire: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(HRSystem), nameof(HRSystem.ButtonConfirmFireEmployee))]
internal static class Patch_HRSystem_ButtonConfirmFireEmployee
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.EmployeeFired); }
        catch (Exception ex) { EventDispatcher.LogError($"ButtonConfirmFireEmployee: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
internal static class Patch_SaveSystem_SaveGame
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.GameSaved); }
        catch (Exception ex) { EventDispatcher.LogError($"SaveGame: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.Load))]
internal static class Patch_SaveSystem_Load
{
    internal static void Postfix()
    {
        try { EventDispatcher.FireSimple(EventIds.GameLoaded); }
        catch (Exception ex) { EventDispatcher.LogError($"Load: {ex.Message}"); }
    }
}
