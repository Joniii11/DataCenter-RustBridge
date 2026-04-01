using MelonLoader;
using MelonLoader.Utils;
using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

[assembly: MelonInfo(typeof(DataCenterModLoader.Core), "DataCenterModLoader", "0.1.0", "DataCenterModding")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace DataCenterModLoader;

public class Core : MelonMod
{
    public static Core Instance { get; private set; }

    private FFIBridge _ffiBridge;
    private string _modsPath;

    public override void OnInitializeMelon()
    {
        Instance = this;
        _modsPath = Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "native");

        LoggerInstance.Msg("╔══════════════════════════════════════════╗");
        LoggerInstance.Msg("║   Data Center Modloader v0.1.0          ║");
        LoggerInstance.Msg("║   Rust FFI Bridge Active                ║");
        LoggerInstance.Msg("╚══════════════════════════════════════════╝");

        if (!Directory.Exists(_modsPath))
        {
            Directory.CreateDirectory(_modsPath);
            LoggerInstance.Msg($"Created Mods/native directory: {_modsPath}");
        }

        _ffiBridge = new FFIBridge(LoggerInstance, _modsPath);
        EventDispatcher.Initialize(_ffiBridge, LoggerInstance);

        try
        {
            HarmonyInstance.PatchAll(typeof(Core).Assembly);
            LoggerInstance.Msg("Harmony patches applied.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"Failed to apply Harmony patches: {ex.Message}");
            LoggerInstance.Msg("Continuing without full event support.");
        }

        _ffiBridge.LoadAllMods();
        LoggerInstance.Msg("Modloader initialization complete.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        _ffiBridge?.OnSceneLoaded(sceneName);
    }

    public override void OnUpdate()
    {
        _ffiBridge?.OnUpdate(Time.deltaTime);
    }

    public override void OnFixedUpdate()
    {
        _ffiBridge?.OnFixedUpdate(Time.fixedDeltaTime);
    }

    public override void OnApplicationQuit()
    {
        LoggerInstance.Msg("Shutting down modloader...");
        _ffiBridge?.Shutdown();
        _ffiBridge?.Dispose();
    }
}
