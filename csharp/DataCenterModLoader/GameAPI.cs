using System;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace DataCenterModLoader;

// function pointer table for rust mods, append-only
[StructLayout(LayoutKind.Sequential)]
public struct GameAPITable
{
    // v1
    public uint ApiVersion;
    public IntPtr LogInfo;
    public IntPtr LogWarning;
    public IntPtr LogError;
    public IntPtr GetPlayerMoney;
    public IntPtr SetPlayerMoney;
    public IntPtr GetTimeScale;
    public IntPtr SetTimeScale;
    public IntPtr GetServerCount;
    public IntPtr GetRackCount;
    public IntPtr GetCurrentScene;

    // v2
    public IntPtr GetPlayerXP;
    public IntPtr SetPlayerXP;
    public IntPtr GetPlayerReputation;
    public IntPtr SetPlayerReputation;
    public IntPtr GetTimeOfDay;
    public IntPtr GetDay;
    public IntPtr GetSecondsInFullDay;
    public IntPtr SetSecondsInFullDay;
    public IntPtr GetSwitchCount;
    public IntPtr GetSatisfiedCustomerCount;

    // v3
    public IntPtr SetNetWatchEnabled;
    public IntPtr IsNetWatchEnabled;
    public IntPtr GetNetWatchStats;

    // v4 — Device & Technician management primitives
    public IntPtr GetBrokenServerCount;
    public IntPtr GetBrokenSwitchCount;
    public IntPtr GetEolServerCount;
    public IntPtr GetEolSwitchCount;
    public IntPtr GetFreeTechnicianCount;
    public IntPtr GetTotalTechnicianCount;
    public IntPtr DispatchRepairServer;
    public IntPtr DispatchRepairSwitch;
    public IntPtr DispatchReplaceServer;
    public IntPtr DispatchReplaceSwitch;
}

// manages the api table, delegates stored as fields to prevent GC
public class GameAPIManager : IDisposable
{
    public const uint API_VERSION = 4;

    private IntPtr _tablePtr;
    private GameAPITable _table;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LogDelegate(IntPtr message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double GetDoubleDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetDoubleDelegate(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetFloatDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFloatDelegate(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetUIntDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetUIntDelegate(uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetStringDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetIntDelegate();

    // prevent GC while rust holds these
    private readonly LogDelegate _logInfo, _logWarning, _logError;
    private readonly GetDoubleDelegate _getPlayerMoney, _getPlayerXP, _getPlayerReputation;
    private readonly SetDoubleDelegate _setPlayerMoney, _setPlayerXP, _setPlayerReputation;
    private readonly GetFloatDelegate _getTimeScale, _getTimeOfDay, _getSecondsInFullDay;
    private readonly SetFloatDelegate _setTimeScale, _setSecondsInFullDay;
    private readonly GetUIntDelegate _getServerCount, _getRackCount, _getDay, _getSwitchCount, _getSatisfiedCustomerCount;
    private readonly GetUIntDelegate _isNetWatchEnabled, _getNetWatchStats;
    private readonly SetUIntDelegate _setNetWatchEnabled;
    private readonly GetStringDelegate _getCurrentScene;
    // v4
    private readonly GetUIntDelegate _getBrokenServerCount, _getBrokenSwitchCount, _getEolServerCount, _getEolSwitchCount;
    private readonly GetUIntDelegate _getFreeTechnicianCount, _getTotalTechnicianCount;
    private readonly GetIntDelegate _dispatchRepairServer, _dispatchRepairSwitch, _dispatchReplaceServer, _dispatchReplaceSwitch;

    private readonly MelonLogger.Instance _logger;
    private IntPtr _currentScenePtr = IntPtr.Zero;

    public GameAPIManager(MelonLogger.Instance logger)
    {
        _logger = logger;

        _logInfo = LogInfoImpl;
        _logWarning = LogWarningImpl;
        _logError = LogErrorImpl;
        _getPlayerMoney = GetPlayerMoneyImpl;
        _setPlayerMoney = SetPlayerMoneyImpl;
        _getTimeScale = GetTimeScaleImpl;
        _setTimeScale = SetTimeScaleImpl;
        _getServerCount = GetServerCountImpl;
        _getRackCount = GetRackCountImpl;
        _getCurrentScene = GetCurrentSceneImpl;
        _getPlayerXP = GetPlayerXPImpl;
        _setPlayerXP = SetPlayerXPImpl;
        _getPlayerReputation = GetPlayerReputationImpl;
        _setPlayerReputation = SetPlayerReputationImpl;
        _getTimeOfDay = GetTimeOfDayImpl;
        _getDay = GetDayImpl;
        _getSecondsInFullDay = GetSecondsInFullDayImpl;
        _setSecondsInFullDay = SetSecondsInFullDayImpl;
        _getSwitchCount = GetSwitchCountImpl;
        _getSatisfiedCustomerCount = GetSatisfiedCustomerCountImpl;
        _setNetWatchEnabled = SetNetWatchEnabledImpl;
        _isNetWatchEnabled = IsNetWatchEnabledImpl;
        _getNetWatchStats = GetNetWatchStatsImpl;

        // v4
        _getBrokenServerCount = GetBrokenServerCountImpl;
        _getBrokenSwitchCount = GetBrokenSwitchCountImpl;
        _getEolServerCount = GetEolServerCountImpl;
        _getEolSwitchCount = GetEolSwitchCountImpl;
        _getFreeTechnicianCount = GetFreeTechnicianCountImpl;
        _getTotalTechnicianCount = GetTotalTechnicianCountImpl;
        _dispatchRepairServer = DispatchRepairServerImpl;
        _dispatchRepairSwitch = DispatchRepairSwitchImpl;
        _dispatchReplaceServer = DispatchReplaceServerImpl;
        _dispatchReplaceSwitch = DispatchReplaceSwitchImpl;

        _table = new GameAPITable
        {
            ApiVersion = API_VERSION,
            LogInfo = Marshal.GetFunctionPointerForDelegate(_logInfo),
            LogWarning = Marshal.GetFunctionPointerForDelegate(_logWarning),
            LogError = Marshal.GetFunctionPointerForDelegate(_logError),
            GetPlayerMoney = Marshal.GetFunctionPointerForDelegate(_getPlayerMoney),
            SetPlayerMoney = Marshal.GetFunctionPointerForDelegate(_setPlayerMoney),
            GetTimeScale = Marshal.GetFunctionPointerForDelegate(_getTimeScale),
            SetTimeScale = Marshal.GetFunctionPointerForDelegate(_setTimeScale),
            GetServerCount = Marshal.GetFunctionPointerForDelegate(_getServerCount),
            GetRackCount = Marshal.GetFunctionPointerForDelegate(_getRackCount),
            GetCurrentScene = Marshal.GetFunctionPointerForDelegate(_getCurrentScene),
            GetPlayerXP = Marshal.GetFunctionPointerForDelegate(_getPlayerXP),
            SetPlayerXP = Marshal.GetFunctionPointerForDelegate(_setPlayerXP),
            GetPlayerReputation = Marshal.GetFunctionPointerForDelegate(_getPlayerReputation),
            SetPlayerReputation = Marshal.GetFunctionPointerForDelegate(_setPlayerReputation),
            GetTimeOfDay = Marshal.GetFunctionPointerForDelegate(_getTimeOfDay),
            GetDay = Marshal.GetFunctionPointerForDelegate(_getDay),
            GetSecondsInFullDay = Marshal.GetFunctionPointerForDelegate(_getSecondsInFullDay),
            SetSecondsInFullDay = Marshal.GetFunctionPointerForDelegate(_setSecondsInFullDay),
            GetSwitchCount = Marshal.GetFunctionPointerForDelegate(_getSwitchCount),
            GetSatisfiedCustomerCount = Marshal.GetFunctionPointerForDelegate(_getSatisfiedCustomerCount),
            SetNetWatchEnabled = Marshal.GetFunctionPointerForDelegate(_setNetWatchEnabled),
            IsNetWatchEnabled = Marshal.GetFunctionPointerForDelegate(_isNetWatchEnabled),
            GetNetWatchStats = Marshal.GetFunctionPointerForDelegate(_getNetWatchStats),
            // v4
            GetBrokenServerCount = Marshal.GetFunctionPointerForDelegate(_getBrokenServerCount),
            GetBrokenSwitchCount = Marshal.GetFunctionPointerForDelegate(_getBrokenSwitchCount),
            GetEolServerCount = Marshal.GetFunctionPointerForDelegate(_getEolServerCount),
            GetEolSwitchCount = Marshal.GetFunctionPointerForDelegate(_getEolSwitchCount),
            GetFreeTechnicianCount = Marshal.GetFunctionPointerForDelegate(_getFreeTechnicianCount),
            GetTotalTechnicianCount = Marshal.GetFunctionPointerForDelegate(_getTotalTechnicianCount),
            DispatchRepairServer = Marshal.GetFunctionPointerForDelegate(_dispatchRepairServer),
            DispatchRepairSwitch = Marshal.GetFunctionPointerForDelegate(_dispatchRepairSwitch),
            DispatchReplaceServer = Marshal.GetFunctionPointerForDelegate(_dispatchReplaceServer),
            DispatchReplaceSwitch = Marshal.GetFunctionPointerForDelegate(_dispatchReplaceSwitch),
        };

        _tablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<GameAPITable>());
        Marshal.StructureToPtr(_table, _tablePtr, false);
    }

    public IntPtr GetTablePointer() => _tablePtr;

    // v1

    private void LogInfoImpl(IntPtr msg) { _logger.Msg("[RustMod] " + (Marshal.PtrToStringAnsi(msg) ?? "")); }
    private void LogWarningImpl(IntPtr msg) { _logger.Warning("[RustMod] " + (Marshal.PtrToStringAnsi(msg) ?? "")); }
    private void LogErrorImpl(IntPtr msg) { _logger.Error("[RustMod] " + (Marshal.PtrToStringAnsi(msg) ?? "")); }

    private double GetPlayerMoneyImpl()
    {
        try { return GameHooks.GetPlayerMoney(); }
        catch (Exception ex) { _logger.Error("GetPlayerMoney: " + ex.Message); return 0.0; }
    }

    private void SetPlayerMoneyImpl(double value)
    {
        try { GameHooks.SetPlayerMoney((float)value); }
        catch (Exception ex) { _logger.Error("SetPlayerMoney: " + ex.Message); }
    }

    private float GetTimeScaleImpl()
    {
        try { return Time.timeScale; } catch { return 1.0f; }
    }

    private void SetTimeScaleImpl(float value)
    {
        try { Time.timeScale = value; }
        catch (Exception ex) { _logger.Error("SetTimeScale: " + ex.Message); }
    }

    private uint GetServerCountImpl() { try { return GameHooks.GetServerCount(); } catch { return 0; } }
    private uint GetRackCountImpl() { try { return GameHooks.GetRackCount(); } catch { return 0; } }

    private IntPtr GetCurrentSceneImpl()
    {
        try
        {
            var name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
            if (_currentScenePtr != IntPtr.Zero) Marshal.FreeHGlobal(_currentScenePtr);
            _currentScenePtr = Marshal.StringToHGlobalAnsi(name);
            return _currentScenePtr;
        }
        catch { return IntPtr.Zero; }
    }

    // v2

    private double GetPlayerXPImpl()
    {
        try { return GameHooks.GetPlayerXP(); }
        catch (Exception ex) { _logger.Error("GetPlayerXP: " + ex.Message); return 0.0; }
    }

    private void SetPlayerXPImpl(double value)
    {
        try { GameHooks.SetPlayerXP((float)value); }
        catch (Exception ex) { _logger.Error("SetPlayerXP: " + ex.Message); }
    }

    private double GetPlayerReputationImpl()
    {
        try { return GameHooks.GetPlayerReputation(); }
        catch (Exception ex) { _logger.Error("GetPlayerReputation: " + ex.Message); return 0.0; }
    }

    private void SetPlayerReputationImpl(double value)
    {
        try { GameHooks.SetPlayerReputation((float)value); }
        catch (Exception ex) { _logger.Error("SetPlayerReputation: " + ex.Message); }
    }

    private float GetTimeOfDayImpl() { try { return GameHooks.GetTimeOfDay(); } catch { return 0f; } }
    private uint GetDayImpl() { try { return (uint)Math.Max(0, GameHooks.GetDay()); } catch { return 0; } }
    private float GetSecondsInFullDayImpl() { try { return GameHooks.GetSecondsInFullDay(); } catch { return 0f; } }

    private void SetSecondsInFullDayImpl(float value)
    {
        try { GameHooks.SetSecondsInFullDay(value); }
        catch (Exception ex) { _logger.Error("SetSecondsInFullDay: " + ex.Message); }
    }

    private uint GetSwitchCountImpl() { try { return GameHooks.GetSwitchCount(); } catch { return 0; } }
    private uint GetSatisfiedCustomerCountImpl() { try { return (uint)Math.Max(0, GameHooks.GetSatisfiedCustomerCount()); } catch { return 0; } }

    // v3 — standalone state (logic moved to Rust mods)
    private static bool _netWatchEnabled;

    private void SetNetWatchEnabledImpl(uint value)
    {
        _netWatchEnabled = value != 0;
    }

    private uint IsNetWatchEnabledImpl() { return _netWatchEnabled ? 1u : 0u; }
    private uint GetNetWatchStatsImpl() { return 0; }

    // v4

    private uint GetBrokenServerCountImpl() { try { return GameHooks.GetBrokenServerCount(); } catch { return 0; } }
    private uint GetBrokenSwitchCountImpl() { try { return GameHooks.GetBrokenSwitchCount(); } catch { return 0; } }
    private uint GetEolServerCountImpl() { try { return GameHooks.GetEolServerCount(); } catch { return 0; } }
    private uint GetEolSwitchCountImpl() { try { return GameHooks.GetEolSwitchCount(); } catch { return 0; } }
    private uint GetFreeTechnicianCountImpl() { try { return GameHooks.GetFreeTechnicianCount(); } catch { return 0; } }
    private uint GetTotalTechnicianCountImpl() { try { return GameHooks.GetTotalTechnicianCount(); } catch { return 0; } }
    private int DispatchRepairServerImpl() { try { return GameHooks.DispatchRepairServer(); } catch { return 0; } }
    private int DispatchRepairSwitchImpl() { try { return GameHooks.DispatchRepairSwitch(); } catch { return 0; } }
    private int DispatchReplaceServerImpl() { try { return GameHooks.DispatchReplaceServer(); } catch { return 0; } }
    private int DispatchReplaceSwitchImpl() { try { return GameHooks.DispatchReplaceSwitch(); } catch { return 0; } }

    public void Dispose()
    {
        if (_tablePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_tablePtr); _tablePtr = IntPtr.Zero; }
        if (_currentScenePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_currentScenePtr); _currentScenePtr = IntPtr.Zero; }
    }
}
