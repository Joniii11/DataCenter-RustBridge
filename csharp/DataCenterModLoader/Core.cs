using MelonLoader;
using MelonLoader.Utils;
using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

[assembly: MelonInfo(typeof(DataCenterModLoader.Core), "RustBridge", "0.1.0", "Joniii")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace DataCenterModLoader;

// file-based crash logger, never throws
public static class CrashLog
{
    private static string _logPath;
    private static readonly object _lock = new();

    public static void Init(string gameRoot)
    {
        try
        {
            _logPath = Path.Combine(gameRoot, "dc_modloader_debug.log");
            var header =
                $"===== RustBridge Debug Log ====={Environment.NewLine}" +
                $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}" +
                $"========================================={Environment.NewLine}";
            File.WriteAllText(_logPath, header);
        }
        catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            if (_logPath == null) return;
            lock (_lock)
            {
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void LogException(string context, Exception ex)
    {
        try
        {
            if (_logPath == null) return;
            lock (_lock)
            {
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] EXCEPTION in {context}:{Environment.NewLine}" +
                    $"  Type: {ex.GetType().FullName}{Environment.NewLine}" +
                    $"  Message: {ex.Message}{Environment.NewLine}" +
                    $"  StackTrace:{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}" +
                    (ex.InnerException != null
                        ? $"  InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}{Environment.NewLine}" +
                          $"  InnerStackTrace:{Environment.NewLine}{ex.InnerException.StackTrace}{Environment.NewLine}"
                        : "") +
                    Environment.NewLine);
            }
        }
        catch { }
    }
}

public class Core : MelonMod
{
    public static Core Instance { get; private set; }

    private FFIBridge _ffiBridge;
    private MultiplayerBridge _mpBridge;
    private string _modsPath;

    public override void OnInitializeMelon()
    {
        try
        {
            Instance = this;

            CrashLog.Init(MelonEnvironment.GameRootDirectory);
            CrashLog.Log("step: CrashLog initialized");

            _modsPath = Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "native");

            LoggerInstance.Msg("╔══════════════════════════════════════════╗");
            LoggerInstance.Msg("║   Rust Bridge v0.1.0                     ║");
            LoggerInstance.Msg("║   Rust FFI Bridge Active                 ║");
            LoggerInstance.Msg("╚══════════════════════════════════════════╝");

            if (!Directory.Exists(_modsPath))
            {
                Directory.CreateDirectory(_modsPath);
                LoggerInstance.Msg($"Created Mods/native directory: {_modsPath}");
            }

            CrashLog.Log("step: creating FFIBridge");
            _ffiBridge = new FFIBridge(LoggerInstance, _modsPath);

            CrashLog.Log("step: initializing EventDispatcher");
            EventDispatcher.Initialize(_ffiBridge, LoggerInstance);

            CrashLog.Log("step: applying Harmony patches");
            try
            {
                HarmonyInstance.PatchAll(typeof(Core).Assembly);
                LoggerInstance.Msg("Harmony patches applied.");
                CrashLog.Log("step: Harmony patches applied successfully");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to apply Harmony patches: {ex.Message}");
                LoggerInstance.Msg("Continuing without full event support.");
                CrashLog.LogException("Harmony patching", ex);
            }

            CrashLog.Log("step: initializing ModConfigSystem");
            ModConfigSystem.Initialize(LoggerInstance);

            CrashLog.Log("step: loading all mods");
            _ffiBridge.LoadAllMods();


            var mpDllPath = Path.Combine(_modsPath, "dc_multiplayer.dll");
            if (File.Exists(mpDllPath))
            {
                _mpBridge = new MultiplayerBridge(LoggerInstance);
            }

            LoggerInstance.Msg("Modloader initialization complete.");
            CrashLog.Log("step: OnInitializeMelon complete");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("OnInitializeMelon", ex);
            throw;
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        try
        {
            _ffiBridge?.OnSceneLoaded(sceneName);
            _mpBridge?.OnSceneLoaded(sceneName);
            ModConfigSystem.OnSceneLoaded(sceneName);
            CustomEmployeeManager.ResetInjectionState();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("OnSceneWasLoaded", ex);
        }
    }

    // Drain the TechnicianManager.pendingDispatches queue periodically so that jobs
    // queued by the game's own "Add all broken devices" button (or restored from a
    // save) are assigned to free technicians even when no CommandCenterOperator is hired.
    private float _queueDrainTimer = 0f;
    private const float QUEUE_DRAIN_INTERVAL = 2f;

    public override void OnUpdate()
    {
        try
        {
            _ffiBridge?.OnUpdate(Time.deltaTime);
            _mpBridge?.OnUpdate(Time.deltaTime);
            ModConfigSystem.OnUpdate(Time.deltaTime);
            CustomEmployeeManager.ReregisterSalariesIfNeeded();
            EntityManager.Update();
            CarryStateMonitor.Update();

            // Periodically force-process any pending dispatch queue entries that
            // the game's ProcessDispatchQueue coroutine would normally handle only
            // when a CommandCenterOperator is hired.
            _queueDrainTimer += Time.deltaTime;
            if (_queueDrainTimer >= QUEUE_DRAIN_INTERVAL)
            {
                _queueDrainTimer = 0f;
                try
                {
                    var tm = Il2Cpp.TechnicianManager.instance;
                    if (tm != null)
                        GameHooks.ForceProcessPendingQueue(tm);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("OnUpdate", ex);
        }


    }

    public override void OnFixedUpdate()
    {
        try
        {
            _ffiBridge?.OnFixedUpdate(Time.fixedDeltaTime);
        }
        catch (Exception ex)
        {
            CrashLog.LogException("OnFixedUpdate", ex);
        }
    }

    public override void OnGUI()
    {
        try
        {
            _mpBridge?.DrawGUI();
            ModConfigSystem.DrawGUI();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("OnGUI", ex);
        }
    }

    public override void OnApplicationQuit()
    {
        try
        {
            LoggerInstance.Msg("Shutting down modloader...");
            CrashLog.Log("step: OnApplicationQuit starting");
            EntityManager.DestroyAll();
            _mpBridge?.Shutdown();
            ModConfigSystem.Shutdown();
            _ffiBridge?.Shutdown();
            _ffiBridge?.Dispose();
            CrashLog.Log("step: OnApplicationQuit complete");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("OnApplicationQuit", ex);
        }
    }
}
