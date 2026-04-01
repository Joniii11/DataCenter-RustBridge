using System;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace DataCenterModLoader;

/// Function pointer table passed to Rust mods. Append-only, never reorder fields.
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
}

/// Manages the GameAPI function pointer table. Delegates are stored as fields to prevent GC.
public class GameAPIManager : IDisposable
{
    public const uint API_VERSION = 2;

    private IntPtr _tablePtr;
    private GameAPITable _table;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LogDelegate(IntPtr message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double GetDoubleDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetDoubleDelegate(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetFloatDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFloatDelegate(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetUIntDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetStringDelegate();

    // Pinned delegate instances (prevent GC while Rust holds raw pointers)
    private readonly LogDelegate _logInfo, _logWarning, _logError;
    private readonly GetDoubleDelegate _getPlayerMoney, _getPlayerXP, _getPlayerReputation;
    private readonly SetDoubleDelegate _setPlayerMoney, _setPlayerXP, _setPlayerReputation;
    private readonly GetFloatDelegate _getTimeScale, _getTimeOfDay, _getSecondsInFullDay;
    private readonly SetFloatDelegate _setTimeScale, _setSecondsInFullDay;
    private readonly GetUIntDelegate _getServerCount, _getRackCount, _getDay, _getSwitchCount, _getSatisfiedCustomerCount;
    private readonly GetStringDelegate _getCurrentScene;

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
        };

        _tablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<GameAPITable>());
        Marshal.StructureToPtr(_table, _tablePtr, false);
    }

    public IntPtr GetTablePointer() => _tablePtr;

    // v1 implementations

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

    // v2 implementations

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

    public void Dispose()
    {
        if (_tablePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_tablePtr); _tablePtr = IntPtr.Zero; }
        if (_currentScenePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_currentScenePtr); _currentScenePtr = IntPtr.Zero; }
    }
}
