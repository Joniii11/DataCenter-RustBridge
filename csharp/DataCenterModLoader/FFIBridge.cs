using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace DataCenterModLoader;

public class FFIBridge : IDisposable
{
    private readonly MelonLogger.Instance _logger;
    private readonly string _modsPath;
    private readonly GameAPIManager _apiManager;
    private readonly List<RustMod> _loadedMods = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ModInfoFFI ModInfoDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool ModInitDelegate(IntPtr apiTable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ModUpdateDelegate(float deltaTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ModOnSceneLoadedDelegate(IntPtr sceneName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ModShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ModOnEventDelegate(uint eventId, IntPtr eventData, uint dataSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct ModInfoFFI
    {
        public IntPtr Id;
        public IntPtr Name;
        public IntPtr Version;
        public IntPtr Author;
        public IntPtr Description;
    }

    private class RustMod
    {
        public string FilePath = "";
        public string Id = "unknown";
        public string Name = "Unknown";
        public string Version = "0.0.0";
        public string Author = "Unknown";
        public IntPtr Handle;
        public ModUpdateDelegate Update;
        public ModUpdateDelegate FixedUpdate;
        public ModOnSceneLoadedDelegate OnSceneLoaded;
        public ModShutdownDelegate Shutdown;
        public ModOnEventDelegate OnEvent;
    }

    public FFIBridge(MelonLogger.Instance logger, string modsPath)
    {
        _logger = logger;
        _modsPath = modsPath;
        _apiManager = new GameAPIManager(logger);
    }

    public void LoadAllMods()
    {
        if (!Directory.Exists(_modsPath))
        {
            _logger.Msg("No Mods/native/ directory found.");
            return;
        }

        var dllFiles = Directory.GetFiles(_modsPath, "*.dll", SearchOption.AllDirectories);
        if (dllFiles.Length == 0)
        {
            _logger.Msg("No native mod DLLs found in Mods/native/.");
            return;
        }

        _logger.Msg($"Found {dllFiles.Length} Rust mod DLL(s).");

        foreach (var dllPath in dllFiles)
        {
            try { LoadMod(dllPath); }
            catch (Exception ex) { _logger.Error($"Failed to load '{Path.GetFileName(dllPath)}': {ex.Message}"); }
        }

        _logger.Msg($"{_loadedMods.Count} Rust mod(s) loaded successfully.");
    }

    private void LoadMod(string dllPath)
    {
        var fileName = Path.GetFileName(dllPath);
        _logger.Msg($"Loading Rust mod: {fileName}");

        var handle = LoadLibrary(dllPath);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Exception($"LoadLibrary failed with error code {error}");
        }

        var mod = new RustMod { FilePath = dllPath, Handle = handle };

        // mod_info
        var modInfoPtr = GetProcAddress(handle, "mod_info");
        if (modInfoPtr != IntPtr.Zero)
        {
            var modInfoFn = Marshal.GetDelegateForFunctionPointer<ModInfoDelegate>(modInfoPtr);
            var info = modInfoFn();

            mod.Id = Marshal.PtrToStringAnsi(info.Id) ?? "unknown";
            mod.Name = Marshal.PtrToStringAnsi(info.Name) ?? "Unknown";
            mod.Version = Marshal.PtrToStringAnsi(info.Version) ?? "0.0.0";
            mod.Author = Marshal.PtrToStringAnsi(info.Author) ?? "Unknown";
            var description = Marshal.PtrToStringAnsi(info.Description) ?? "";

            _logger.Msg($"  Mod: {mod.Name} v{mod.Version} by {mod.Author}");
            _logger.Msg($"  Description: {description}");
        }
        else
        {
            _logger.Warning($"  '{fileName}' has no mod_info() export.");
        }

        // mod_init
        var modInitPtr = GetProcAddress(handle, "mod_init");
        if (modInitPtr != IntPtr.Zero)
        {
            var modInitFn = Marshal.GetDelegateForFunctionPointer<ModInitDelegate>(modInitPtr);
            if (!modInitFn(_apiManager.GetTablePointer()))
            {
                _logger.Error($"  Mod '{mod.Name}' mod_init() returned false.");
                FreeLibrary(handle);
                return;
            }
            _logger.Msg($"  Mod '{mod.Name}' initialized.");
        }
        else
        {
            _logger.Warning($"  '{fileName}' has no mod_init() export.");
        }

        // Optional exports
        var updatePtr = GetProcAddress(handle, "mod_update");
        if (updatePtr != IntPtr.Zero)
            mod.Update = Marshal.GetDelegateForFunctionPointer<ModUpdateDelegate>(updatePtr);

        var fixedUpdatePtr = GetProcAddress(handle, "mod_fixed_update");
        if (fixedUpdatePtr != IntPtr.Zero)
            mod.FixedUpdate = Marshal.GetDelegateForFunctionPointer<ModUpdateDelegate>(fixedUpdatePtr);

        var sceneLoadedPtr = GetProcAddress(handle, "mod_on_scene_loaded");
        if (sceneLoadedPtr != IntPtr.Zero)
            mod.OnSceneLoaded = Marshal.GetDelegateForFunctionPointer<ModOnSceneLoadedDelegate>(sceneLoadedPtr);

        var shutdownPtr = GetProcAddress(handle, "mod_shutdown");
        if (shutdownPtr != IntPtr.Zero)
            mod.Shutdown = Marshal.GetDelegateForFunctionPointer<ModShutdownDelegate>(shutdownPtr);

        var onEventPtr = GetProcAddress(handle, "mod_on_event");
        if (onEventPtr != IntPtr.Zero)
        {
            mod.OnEvent = Marshal.GetDelegateForFunctionPointer<ModOnEventDelegate>(onEventPtr);
            _logger.Msg($"  Mod '{mod.Name}' supports game events.");
        }

        _loadedMods.Add(mod);
    }

    public void OnUpdate(float deltaTime)
    {
        foreach (var mod in _loadedMods)
        {
            try { mod.Update?.Invoke(deltaTime); }
            catch (Exception ex) { _logger.Error($"[{mod.Name}] mod_update crashed: {ex.Message}"); }
        }
    }

    public void OnFixedUpdate(float deltaTime)
    {
        foreach (var mod in _loadedMods)
        {
            try { mod.FixedUpdate?.Invoke(deltaTime); }
            catch (Exception ex) { _logger.Error($"[{mod.Name}] mod_fixed_update crashed: {ex.Message}"); }
        }
    }

    public void OnSceneLoaded(string sceneName)
    {
        var ptr = Marshal.StringToHGlobalAnsi(sceneName);
        try
        {
            foreach (var mod in _loadedMods)
            {
                try { mod.OnSceneLoaded?.Invoke(ptr); }
                catch (Exception ex) { _logger.Error($"[{mod.Name}] mod_on_scene_loaded crashed: {ex.Message}"); }
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public void DispatchEvent(uint eventId, IntPtr eventData, uint dataSize)
    {
        foreach (var mod in _loadedMods)
        {
            if (mod.OnEvent == null) continue;
            try { mod.OnEvent.Invoke(eventId, eventData, dataSize); }
            catch (Exception ex) { _logger.Error($"[{mod.Name}] mod_on_event(id={eventId}) crashed: {ex.Message}"); }
        }
    }

    public void Shutdown()
    {
        foreach (var mod in _loadedMods)
        {
            try
            {
                _logger.Msg($"Shutting down: {mod.Name}");
                mod.Shutdown?.Invoke();
            }
            catch (Exception ex) { _logger.Error($"[{mod.Name}] mod_shutdown crashed: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        foreach (var mod in _loadedMods)
        {
            if (mod.Handle != IntPtr.Zero)
                FreeLibrary(mod.Handle);
        }
        _loadedMods.Clear();
        _apiManager.Dispose();
    }
}
