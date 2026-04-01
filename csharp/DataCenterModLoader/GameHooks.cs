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
}
