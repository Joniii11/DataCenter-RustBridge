using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;

namespace DataCenterModLoader;

public static class ModConfigSystem
{
    private enum ConfigEntryType
    {
        Bool,
        Int,
        Float
    }

    private class ConfigEntry
    {
        public ConfigEntryType Type;
        public string Key;
        public string DisplayName;
        public string Description;

        // Bool
        public bool BoolValue;
        public bool BoolDefault;

        // Int
        public int IntValue;
        public int IntDefault;
        public int IntMin;
        public int IntMax;

        // Float
        public float FloatValue;
        public float FloatDefault;
        public float FloatMin;
        public float FloatMax;
    }

    private class ModConfig
    {
        public string ModId;
        public string Author = "";
        public string Version = "";
        public Dictionary<string, ConfigEntry> Entries = new();
        public List<string> EntryOrder = new();
    }

    private static MelonLogger.Instance _logger;
    private static string _configDir;
    private static readonly Dictionary<string, ModConfig> _mods = new();
    private static readonly List<string> _modOrder = new();
    private static bool _initialized;

    private static bool _showPanel;
    private static string _selectedModId;
    private static float _scrollOffset;
    private static bool _stylesInitialized;

    private static bool _isDragging;
    private static Vector2 _dragOffset;
    private static float _panelX = -1f;
    private static float _panelY = -1f;

    private static UnityEngine.EventSystems.EventSystem _disabledEventSystem;
    private static int _reenableEventSystemCountdown;

    private static bool _showSettingsChoice;
    private static Il2Cpp.MainMenu _mainMenuRef;

    private static bool _pendingSettingsIntercept;
    private static float _settingsInterceptTimer;
    private static bool _deferredOpenGameSettings;

    private static bool _pendingPauseMenuInject;
    private static float _pauseMenuInjectTimer;
    private static GameObject _pauseMenuModButton;


    private static GUIStyle _windowStyle;
    private static GUIStyle _titleStyle;
    private static GUIStyle _labelStyle;
    private static GUIStyle _descriptionStyle;
    private static GUIStyle _buttonStyle;
    private static GUIStyle _smallButtonStyle;
    private static GUIStyle _modListButtonStyle;
    private static GUIStyle _modListSelectedStyle;
    private static GUIStyle _toggleOnStyle;
    private static GUIStyle _toggleOffStyle;
    private static GUIStyle _valueStyle;
    private static GUIStyle _rangeLabelStyle;
    private static GUIStyle _closeButtonStyle;

    private static Texture2D _windowBg;
    private static Texture2D _buttonBg;
    private static Texture2D _buttonHoverBg;
    private static Texture2D _fieldBg;
    private static Texture2D _toggleOnBg;
    private static Texture2D _toggleOnHoverBg;
    private static Texture2D _toggleOffBg;
    private static Texture2D _toggleOffHoverBg;
    private static Texture2D _modListBg;
    private static Texture2D _modListHoverBg;
    private static Texture2D _modListSelectedBg;
    private static Texture2D _sidebarBg;

    public static bool IsPanelVisible => _showPanel;

    public static void Initialize(MelonLogger.Instance logger)
    {
        try
        {
            _logger = logger;
            _configDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ModConfigs");

            if (!Directory.Exists(_configDir))
            {
                Directory.CreateDirectory(_configDir);
            }

            _initialized = true;
            CrashLog.Log("ModConfig: initialized, config dir = " + _configDir);
            _logger.Msg("[ModConfig] Configuration system initialized.");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.Initialize", ex);
        }
    }

    public static void OnUpdate(float dt)
    {
        if (_pendingSettingsIntercept)
        {
            _settingsInterceptTimer -= dt;
            if (_settingsInterceptTimer <= 0f)
            {
                _pendingSettingsIntercept = false;
                InterceptSettingsButton();
            }
        }

        if (_pendingPauseMenuInject)
        {
            _pauseMenuInjectTimer -= dt;
            if (_pauseMenuInjectTimer <= 0f)
            {
                _pendingPauseMenuInject = false;
                InjectPauseMenuButton();
            }
        }

        // Wait N frames so the closing click doesn't pass through to the game
        if (_reenableEventSystemCountdown > 0)
        {
            _reenableEventSystemCountdown--;
            if (_reenableEventSystemCountdown <= 0)
            {
                try
                {
                    if (_disabledEventSystem != null)
                    {
                        _disabledEventSystem.enabled = true;
                        _disabledEventSystem = null;
                    }
                }
                catch { }
            }
        }

        // Deferred from IMGUI - must run outside OnGUI
        if (_deferredOpenGameSettings)
        {
            _deferredOpenGameSettings = false;

            // Game settings need EventSystem immediately - cancel any deferred countdown
            _reenableEventSystemCountdown = 0;
            try
            {
                if (_disabledEventSystem != null)
                {
                    _disabledEventSystem.enabled = true;
                    _disabledEventSystem = null;
                }
            }
            catch { }

            try
            {
                if (_mainMenuRef != null)
                {
                    CrashLog.Log("ModConfig: opening game settings (direct call, no Harmony).");
                    _mainMenuRef.Settings();
                }
                else
                {
                    CrashLog.Log("ModConfig: _mainMenuRef was null - cannot open game settings.");
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("ModConfigSystem deferred game settings open", ex);
            }
        }

        // F8 hotkey to toggle panel, ESC to close (uses new Input System - legacy Input throws in this game)
        try
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f8Key.wasPressedThisFrame)
                {
                    if (_showPanel)
                        HidePanel();
                    else
                        ShowPanel();
                }
                else if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_showPanel)
                        HidePanel();
                    else if (_showSettingsChoice)
                        HideSettingsChoice();
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.OnUpdate (hotkey)", ex);
        }
    }

    public static void DrawGUI()
    {
        try
        {
            if (!_showPanel && !_showSettingsChoice) return;

            if (!_stylesInitialized)
                InitStyles();

            if (_showSettingsChoice)
                DrawSettingsChoice();

            if (_showPanel)
                DrawPanel();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.DrawGUI", ex);
        }
    }

    public static void OnSceneLoaded(string sceneName)
    {
        try
        {
            CrashLog.Log($"ModConfig: scene '{sceneName}' loaded.");

            if (sceneName == "MainMenu")
            {
                _pendingSettingsIntercept = true;
                _settingsInterceptTimer = 0.6f;
                _pauseMenuModButton = null;
                CrashLog.Log("ModConfig: will intercept Settings button in 0.6s.");
            }
            else
            {
                // Game scene - inject "Mod Settings" button into the pause menu
                _pendingPauseMenuInject = true;
                _pauseMenuInjectTimer = 1.0f;
                _pauseMenuModButton = null;
                CrashLog.Log("ModConfig: will inject pause-menu Mod Settings button in 1.0s.");
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.OnSceneLoaded", ex);
        }
    }

    public static void Shutdown()
    {
        try
        {
            foreach (var kvp in _mods)
            {
                SaveModConfig(kvp.Value);
            }
            CrashLog.Log("ModConfig: shutdown, all configs saved.");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.Shutdown", ex);
        }
    }

    public static void ShowPanel()
    {
        _showPanel = true;
        _scrollOffset = 0f;
        _isDragging = false;

        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                _disabledEventSystem = es;
                es.enabled = false;
            }
        }
        catch { }

        CrashLog.Log("ModConfig: panel shown.");
    }

    public static void HidePanel()
    {
        _showPanel = false;

        // Defer re-enabling EventSystem by 2 frames so the mouse-up from the
        // Close / X click doesn't pass through to the game button underneath.
        if (_disabledEventSystem != null)
            _reenableEventSystemCountdown = 2;

        CrashLog.Log("ModConfig: panel hidden.");
    }

    public static void ShowSettingsChoice()
    {
        _showSettingsChoice = true;

        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                _disabledEventSystem = es;
                es.enabled = false;
            }
        }
        catch { }

        CrashLog.Log("ModConfig: settings choice popup shown.");
    }

    // Replaces the Harmony prefix approach which had static-field visibility issues in Il2Cpp Harmony.
    private static void InterceptSettingsButton()
    {
        try
        {
            // Find the "Settings" label among all TMP texts (same approach as MultiplayerBridge)
            var allTexts = Resources.FindObjectsOfTypeAll<Il2CppTMPro.TextMeshProUGUI>();
            Transform settingsBtn = null;

            foreach (var tmp in allTexts)
            {
                if (tmp.text == "Settings")
                {
                    settingsBtn = tmp.transform.parent;
                    break;
                }
            }

            if (settingsBtn == null)
            {
                CrashLog.Log("ModConfig: could not find 'Settings' button for interception.");
                return;
            }

            // Capture the MainMenu component so we can call Settings() later
            var menus = Resources.FindObjectsOfTypeAll<Il2Cpp.MainMenu>();
            if (menus != null && menus.Count > 0)
            {
                _mainMenuRef = menus[0];
                CrashLog.Log("ModConfig: captured MainMenu reference.");
            }
            else
            {
                CrashLog.Log("ModConfig: WARNING - no MainMenu instance found.");
            }

            var btnExt = settingsBtn.GetComponent<ButtonExtended>();
            if (btnExt != null)
            {
                btnExt.onClick = new ButtonExtended.ButtonClickedEvent();
                btnExt.onClick.AddListener((System.Action)(() => ShowSettingsChoice()));
                CrashLog.Log("ModConfig: Settings button onClick replaced (ButtonExtended).");
            }
            else
            {
                // Fallback to standard Unity Button
                var btn = settingsBtn.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((System.Action)(() => ShowSettingsChoice()));
                    CrashLog.Log("ModConfig: Settings button onClick replaced (Button fallback).");
                }
                else
                {
                    CrashLog.Log("ModConfig: no ButtonExtended or Button found on Settings button.");
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.InterceptSettingsButton", ex);
        }
    }

    // Uses component hierarchy - LocalisedText hasn't set button labels yet when UI is inactive.
    private static void InjectPauseMenuButton()
    {
        try
        {
            if (_pauseMenuModButton != null) return;

            var pauseMenus = Resources.FindObjectsOfTypeAll<PauseMenu>();
            if (pauseMenus == null || pauseMenus.Count == 0)
            {
                CrashLog.Log("ModConfig: no PauseMenu instance found - cannot inject button.");
                return;
            }

            var pauseMenu = pauseMenus[0];
            var pmUI = pauseMenu.pauseMenuUI;
            if (pmUI == null)
            {
                CrashLog.Log("ModConfig: PauseMenu.pauseMenuUI is null.");
                return;
            }

            CrashLog.Log($"ModConfig: found PauseMenu, pauseMenuUI = '{pmUI.name}'.");

            // The pause menu has two kinds of ButtonExtended:
            //   - Settings tab buttons (System, Audio, etc.) which ALSO have PauseMenu_TabButton
            //   - Action buttons (Resume, Save Game, etc.) which do NOT have PauseMenu_TabButton
            // We only want the action buttons.
            var allButtons = pmUI.GetComponentsInChildren<ButtonExtended>(true);
            if (allButtons == null || allButtons.Count == 0)
            {
                CrashLog.Log("ModConfig: no ButtonExtended found in pauseMenuUI.");
                return;
            }

            // Filter out tab buttons - keep only pure action buttons
            var actionButtons = new System.Collections.Generic.List<ButtonExtended>();
            foreach (var be in allButtons)
            {
                if (be.GetComponent<PauseMenu_TabButton>() == null)
                    actionButtons.Add(be);
            }

            CrashLog.Log($"ModConfig: found {allButtons.Count} ButtonExtended(s), {actionButtons.Count} are action buttons (non-tab).");

            if (actionButtons.Count == 0)
            {
                CrashLog.Log("ModConfig: no action buttons found in pause menu.");
                return;
            }

            Transform templateBtn = null;
            Transform buttonPanel = null;

            foreach (var be in actionButtons)
            {
                var parent = be.transform.parent;
                if (parent == null) continue;

                // Collect action buttons; skip Resume (first) since it's oversized
                var panelActionBtns = new System.Collections.Generic.List<Transform>();
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    var childBtn = child.GetComponent<ButtonExtended>();
                    if (childBtn != null && child.GetComponent<PauseMenu_TabButton>() == null)
                        panelActionBtns.Add(child);
                }

                if (panelActionBtns.Count >= 4)
                {
                    templateBtn = panelActionBtns.Count > 1 ? panelActionBtns[1] : panelActionBtns[0];
                    buttonPanel = parent;
                    CrashLog.Log($"ModConfig: using action button panel '{parent.name}' ({panelActionBtns.Count} action buttons), template='{templateBtn.name}'.");
                    break;
                }
            }

            if (templateBtn == null || buttonPanel == null)
            {
                // Fallback: just use the first action button and its parent
                templateBtn = actionButtons[0].transform;
                buttonPanel = templateBtn.parent;
                CrashLog.Log($"ModConfig: fallback - using first action button in '{buttonPanel?.name}'.");
            }

            if (buttonPanel == null)
            {
                CrashLog.Log("ModConfig: button panel is null - cannot inject.");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(templateBtn.gameObject, buttonPanel);
            clone.name = "ModSettingsButton";

            // Place before "Quit to desktop"
            int childCount = buttonPanel.childCount;
            if (childCount >= 3)
                clone.transform.SetSiblingIndex(childCount - 3);

            // Destroy LocalisedText so our label sticks
            var locTexts = clone.GetComponentsInChildren<LocalisedText>(true);
            if (locTexts != null)
            {
                foreach (var lt in locTexts)
                    UnityEngine.Object.Destroy(lt);
            }

            var cloneTexts = clone.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            if (cloneTexts != null)
            {
                foreach (var t in cloneTexts)
                {
                    t.text = "Mod Settings";
                    try { t.SetText("Mod Settings"); } catch { }
                    try { t.ForceMeshUpdate(); } catch { }
                }
            }

            var btnExt = clone.GetComponent<ButtonExtended>();
            if (btnExt != null)
            {
                btnExt.onClick = new ButtonExtended.ButtonClickedEvent();
                btnExt.onClick.AddListener((System.Action)(() => ShowPanel()));
                CrashLog.Log("ModConfig: pause menu Mod Settings wired (ButtonExtended).");
            }
            else
            {
                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((System.Action)(() => ShowPanel()));
                    CrashLog.Log("ModConfig: pause menu Mod Settings wired (Button fallback).");
                }
                else
                {
                    CrashLog.Log("ModConfig: no ButtonExtended or Button on cloned pause menu button.");
                }
            }

            _pauseMenuModButton = clone;
            CrashLog.Log("ModConfig: Mod Settings button injected into pause menu.");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.InjectPauseMenuButton", ex);
        }
    }

    private static void HideSettingsChoice()
    {
        _showSettingsChoice = false;

        // Defer re-enabling EventSystem by 2 frames to prevent click-through
        if (_disabledEventSystem != null)
            _reenableEventSystemCountdown = 2;

        CrashLog.Log("ModConfig: settings choice popup hidden.");
    }

    private static void DrawSettingsChoice()
    {
        float popW = 340f, popH = 220f;
        float popX = (Screen.width - popW) / 2f;
        float popY = (Screen.height - popH) / 2f;
        var popRect = new Rect(popX, popY, popW, popH);

        GUI.DrawTexture(popRect, _windowBg);

        float pad = 24f;
        float btnH = 48f;
        float btnW = popW - pad * 2;

        GUI.Label(new Rect(popX + pad, popY + pad, btnW, 28f), "SETTINGS", _titleStyle);
        GUI.DrawTexture(new Rect(popX + pad, popY + pad + 34f, btnW, 1f), _fieldBg);

        float y = popY + pad + 48f;

        if (GUI.Button(new Rect(popX + pad, y, btnW, btnH), "Game Settings", _closeButtonStyle))
        {
            CrashLog.Log("ModConfig: 'Game Settings' clicked - deferring to next frame.");
            HideSettingsChoice();
            _deferredOpenGameSettings = true;
        }

        y += btnH + 12f;

        if (GUI.Button(new Rect(popX + pad, y, btnW, btnH), "Mod Settings", _closeButtonStyle))
        {
            _showSettingsChoice = false;
            // EventSystem stays disabled - ShowPanel expects it
            ShowPanel();
        }

        var evt = Event.current;
        if (evt.type == EventType.MouseDown && evt.button == 0 && !popRect.Contains(evt.mousePosition))
        {
            HideSettingsChoice();
            evt.Use();
        }
    }

    public static uint RegisterBool(string modId, string key, string displayName, bool defaultValue, string description)
    {
        try
        {
            var mod = GetOrCreateMod(modId);

            if (mod.Entries.ContainsKey(key))
            {
                CrashLog.Log($"ModConfig: RegisterBool '{modId}/{key}' already exists, returning 0.");
                return 0;
            }

            var entry = new ConfigEntry
            {
                Type = ConfigEntryType.Bool,
                Key = key,
                DisplayName = displayName ?? key,
                Description = description ?? "",
                BoolDefault = defaultValue,
                BoolValue = defaultValue
            };

            var persisted = LoadPersistedValue(modId, key);
            if (persisted != null && persisted.Type == ConfigEntryType.Bool)
            {
                entry.BoolValue = persisted.BoolValue;
            }

            mod.Entries[key] = entry;
            mod.EntryOrder.Add(key);

            CrashLog.Log($"ModConfig: RegisterBool '{modId}/{key}' = {entry.BoolValue} (default={defaultValue})");
            _logger.Msg($"[ModConfig] Registered bool '{modId}/{key}': {entry.BoolValue}");
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.RegisterBool", ex);
            return 0;
        }
    }

    public static uint RegisterInt(string modId, string key, string displayName, int defaultValue, int min, int max, string description)
    {
        try
        {
            var mod = GetOrCreateMod(modId);

            if (mod.Entries.ContainsKey(key))
            {
                CrashLog.Log($"ModConfig: RegisterInt '{modId}/{key}' already exists, returning 0.");
                return 0;
            }

            var entry = new ConfigEntry
            {
                Type = ConfigEntryType.Int,
                Key = key,
                DisplayName = displayName ?? key,
                Description = description ?? "",
                IntDefault = defaultValue,
                IntValue = defaultValue,
                IntMin = min,
                IntMax = max
            };

            var persisted = LoadPersistedValue(modId, key);
            if (persisted != null && persisted.Type == ConfigEntryType.Int)
            {
                entry.IntValue = Math.Clamp(persisted.IntValue, min, max);
            }

            mod.Entries[key] = entry;
            mod.EntryOrder.Add(key);

            CrashLog.Log($"ModConfig: RegisterInt '{modId}/{key}' = {entry.IntValue} (default={defaultValue}, range={min}-{max})");
            _logger.Msg($"[ModConfig] Registered int '{modId}/{key}': {entry.IntValue}");
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.RegisterInt", ex);
            return 0;
        }
    }

    public static uint RegisterFloat(string modId, string key, string displayName, float defaultValue, float min, float max, string description)
    {
        try
        {
            var mod = GetOrCreateMod(modId);

            if (mod.Entries.ContainsKey(key))
            {
                CrashLog.Log($"ModConfig: RegisterFloat '{modId}/{key}' already exists, returning 0.");
                return 0;
            }

            var entry = new ConfigEntry
            {
                Type = ConfigEntryType.Float,
                Key = key,
                DisplayName = displayName ?? key,
                Description = description ?? "",
                FloatDefault = defaultValue,
                FloatValue = defaultValue,
                FloatMin = min,
                FloatMax = max
            };

            var persisted = LoadPersistedValue(modId, key);
            if (persisted != null && persisted.Type == ConfigEntryType.Float)
            {
                entry.FloatValue = Math.Clamp(persisted.FloatValue, min, max);
            }

            mod.Entries[key] = entry;
            mod.EntryOrder.Add(key);

            CrashLog.Log($"ModConfig: RegisterFloat '{modId}/{key}' = {entry.FloatValue:F1} (default={defaultValue:F1}, range={min:F1}-{max:F1})");
            _logger.Msg($"[ModConfig] Registered float '{modId}/{key}': {entry.FloatValue:F1}");
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.RegisterFloat", ex);
            return 0;
        }
    }

    public static uint GetBool(string modId, string key)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Bool)
                    return entry.BoolValue ? 1u : 0u;
            }
            return 0xFFFFFFFF;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.GetBool", ex);
            return 0xFFFFFFFF;
        }
    }

    public static int GetInt(string modId, string key)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Int)
                    return entry.IntValue;
            }
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.GetInt", ex);
            return 0;
        }
    }

    public static float GetFloat(string modId, string key)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Float)
                    return entry.FloatValue;
            }
            return 0f;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.GetFloat", ex);
            return 0f;
        }
    }

    public static void SetModInfo(string modId, string author, string version)
    {
        try
        {
            if (string.IsNullOrEmpty(modId)) return;
            var mod = GetOrCreateMod(modId);
            if (!string.IsNullOrEmpty(author)) mod.Author = author;
            if (!string.IsNullOrEmpty(version)) mod.Version = version;
            CrashLog.Log($"ModConfig: set mod info for '{modId}': author='{author}', version='{version}'");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.SetModInfo", ex);
        }
    }

    public static bool RegisterBoolOption(string modId, string key, string displayName, bool defaultValue, string description = "")
        => RegisterBool(modId, key, displayName, defaultValue, description) == 1;

    public static bool RegisterIntOption(string modId, string key, string displayName, int defaultValue, int min, int max, string description = "")
        => RegisterInt(modId, key, displayName, defaultValue, min, max, description) == 1;

    public static bool RegisterFloatOption(string modId, string key, string displayName, float defaultValue, float min, float max, string description = "")
        => RegisterFloat(modId, key, displayName, defaultValue, min, max, description) == 1;

    public static bool GetBoolValue(string modId, string key, bool defaultValue = false)
    {
        uint raw = GetBool(modId, key);
        if (raw == 0xFFFFFFFF) return defaultValue;
        return raw == 1;
    }

    public static int GetIntValue(string modId, string key, int defaultValue = 0)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Int)
                    return entry.IntValue;
            }
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    public static float GetFloatValue(string modId, string key, float defaultValue = 0f)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Float)
                    return entry.FloatValue;
            }
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    public static bool SetBoolValue(string modId, string key, bool value)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Bool)
                {
                    entry.BoolValue = value;
                    SaveModConfig(mod);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.SetBoolValue", ex);
            return false;
        }
    }

    public static bool SetIntValue(string modId, string key, int value)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Int)
                {
                    entry.IntValue = Math.Clamp(value, entry.IntMin, entry.IntMax);
                    SaveModConfig(mod);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.SetIntValue", ex);
            return false;
        }
    }

    public static bool SetFloatValue(string modId, string key, float value)
    {
        try
        {
            if (_mods.TryGetValue(modId, out var mod) && mod.Entries.TryGetValue(key, out var entry))
            {
                if (entry.Type == ConfigEntryType.Float)
                {
                    entry.FloatValue = Math.Clamp(value, entry.FloatMin, entry.FloatMax);
                    SaveModConfig(mod);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.SetFloatValue", ex);
            return false;
        }
    }

    public static bool HasOption(string modId, string key)
    {
        return _mods.TryGetValue(modId, out var mod) && mod.Entries.ContainsKey(key);
    }

    public static void OpenPanel() => ShowPanel();
    public static void ClosePanel() => HidePanel();

    private static ModConfig GetOrCreateMod(string modId)
    {
        if (!_mods.TryGetValue(modId, out var mod))
        {
            mod = new ModConfig { ModId = modId };
            _mods[modId] = mod;
            _modOrder.Add(modId);

            if (_selectedModId == null)
                _selectedModId = modId;

            CrashLog.Log($"ModConfig: created mod config for '{modId}'.");
        }
        return mod;
    }

    private static string GetConfigPath(string modId)
    {
        return Path.Combine(_configDir, modId + ".json");
    }

    private static void SaveModConfig(ModConfig mod)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"entries\": {");

            bool first = true;
            foreach (var key in mod.EntryOrder)
            {
                if (!mod.Entries.TryGetValue(key, out var entry))
                    continue;

                if (!first) sb.AppendLine(",");
                first = false;

                string escapedKey = EscapeJsonString(key);

                switch (entry.Type)
                {
                    case ConfigEntryType.Bool:
                        sb.Append($"    \"{escapedKey}\": {{ \"type\": \"bool\", \"value\": {(entry.BoolValue ? "true" : "false")} }}");
                        break;
                    case ConfigEntryType.Int:
                        sb.Append($"    \"{escapedKey}\": {{ \"type\": \"int\", \"value\": {entry.IntValue} }}");
                        break;
                    case ConfigEntryType.Float:
                        sb.Append($"    \"{escapedKey}\": {{ \"type\": \"float\", \"value\": {entry.FloatValue.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
                        break;
                }
            }

            if (!first) sb.AppendLine();
            sb.AppendLine("  }");
            sb.AppendLine("}");

            string path = GetConfigPath(mod.ModId);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            CrashLog.Log($"ModConfig: saved config for '{mod.ModId}' to {path}");
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"ModConfigSystem.SaveModConfig({mod.ModId})", ex);
        }
    }

    private static ConfigEntry LoadPersistedValue(string modId, string key)
    {
        try
        {
            string path = GetConfigPath(modId);
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path, Encoding.UTF8);
            var entries = ParseConfigJson(json);

            if (entries != null && entries.TryGetValue(key, out var entry))
                return entry;
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"ModConfigSystem.LoadPersistedValue({modId}/{key})", ex);
        }
        return null;
    }

    private static Dictionary<string, ConfigEntry> ParseConfigJson(string json)
    {
        var result = new Dictionary<string, ConfigEntry>();

        try
        {
            // Find the "entries" object
            int entriesIdx = json.IndexOf("\"entries\"", StringComparison.Ordinal);
            if (entriesIdx < 0) return result;

            // Find the opening brace of entries object
            int braceStart = json.IndexOf('{', entriesIdx + 9);
            if (braceStart < 0) return result;

            // Find the matching closing brace
            int braceEnd = FindMatchingBrace(json, braceStart);
            if (braceEnd < 0) return result;

            string entriesBlock = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            // Parse each entry: "key": { "type": "...", "value": ... }
            int pos = 0;
            while (pos < entriesBlock.Length)
            {
                // Find the next key
                int keyStart = entriesBlock.IndexOf('"', pos);
                if (keyStart < 0) break;

                int keyEnd = entriesBlock.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string entryKey = UnescapeJsonString(entriesBlock.Substring(keyStart + 1, keyEnd - keyStart - 1));

                // Find the entry object opening brace
                int entryBraceStart = entriesBlock.IndexOf('{', keyEnd);
                if (entryBraceStart < 0) break;

                int entryBraceEnd = FindMatchingBrace(entriesBlock, entryBraceStart);
                if (entryBraceEnd < 0) break;

                string entryBody = entriesBlock.Substring(entryBraceStart + 1, entryBraceEnd - entryBraceStart - 1);

                // Parse type
                string type = ExtractJsonStringValue(entryBody, "type");
                if (type == null)
                {
                    pos = entryBraceEnd + 1;
                    continue;
                }

                // Parse value
                string valueStr = ExtractJsonRawValue(entryBody, "value");
                if (valueStr == null)
                {
                    pos = entryBraceEnd + 1;
                    continue;
                }

                var entry = new ConfigEntry { Key = entryKey };

                switch (type)
                {
                    case "bool":
                        entry.Type = ConfigEntryType.Bool;
                        entry.BoolValue = valueStr.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "int":
                        entry.Type = ConfigEntryType.Int;
                        if (int.TryParse(valueStr.Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out int iv))
                            entry.IntValue = iv;
                        break;
                    case "float":
                        entry.Type = ConfigEntryType.Float;
                        if (float.TryParse(valueStr.Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out float fv))
                            entry.FloatValue = fv;
                        break;
                    default:
                        pos = entryBraceEnd + 1;
                        continue;
                }

                result[entryKey] = entry;
                pos = entryBraceEnd + 1;
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("ModConfigSystem.ParseConfigJson", ex);
        }

        return result;
    }

    private static int FindMatchingBrace(string text, int openPos)
    {
        int depth = 0;
        for (int i = openPos; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;

            if (depth == 0) return i;
        }
        return -1;
    }

    private static string ExtractJsonStringValue(string body, string key)
    {
        string pattern = "\"" + key + "\"";
        int idx = body.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return null;

        int colonIdx = body.IndexOf(':', idx + pattern.Length);
        if (colonIdx < 0) return null;

        // Find the opening quote of the value
        int quoteStart = body.IndexOf('"', colonIdx + 1);
        if (quoteStart < 0) return null;

        int quoteEnd = body.IndexOf('"', quoteStart + 1);
        if (quoteEnd < 0) return null;

        return body.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
    }

    private static string ExtractJsonRawValue(string body, string key)
    {
        string pattern = "\"" + key + "\"";
        int idx = body.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return null;

        int colonIdx = body.IndexOf(':', idx + pattern.Length);
        if (colonIdx < 0) return null;

        // Skip whitespace after colon
        int start = colonIdx + 1;
        while (start < body.Length && (body[start] == ' ' || body[start] == '\t'))
            start++;

        if (start >= body.Length) return null;

        // If it's a quoted string, extract it
        if (body[start] == '"')
        {
            int quoteEnd = body.IndexOf('"', start + 1);
            if (quoteEnd < 0) return null;
            return body.Substring(start + 1, quoteEnd - start - 1);
        }

        // Otherwise read until comma, brace, bracket, or end
        int end = start;
        while (end < body.Length && body[end] != ',' && body[end] != '}' && body[end] != ']'
               && body[end] != '\r' && body[end] != '\n')
            end++;

        return body.Substring(start, end - start).Trim();
    }

    private static string EscapeJsonString(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string UnescapeJsonString(string s)
    {
        if (s == null) return "";
        return s.Replace("\\\"", "\"").Replace("\\\\", "\\")
                .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
    }


    private static bool IsShiftHeld()
    {
        try
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }
        catch { return false; }
    }

    private static void DrawPanel()
    {
        float w = 700f, h = 740f;

        if (_panelX < 0f || _panelY < 0f)
        {
            _panelX = (Screen.width - w) / 2f;
            _panelY = (Screen.height - h) / 2f;
        }

        _panelX = Mathf.Clamp(_panelX, 0f, Screen.width - w);
        _panelY = Mathf.Clamp(_panelY, 0f, Screen.height - h);

        float px = _panelX;
        float py = _panelY;
        var panelRect = new Rect(px, py, w, h);

        // ── Dragging logic on title bar ──
        var titleBarRect = new Rect(px, py, w - 40f, 40f); // leave room for X button
        var evt = Event.current;

        if (evt.type == EventType.MouseDown && evt.button == 0 && titleBarRect.Contains(evt.mousePosition))
        {
            _isDragging = true;
            _dragOffset = new Vector2(evt.mousePosition.x - px, evt.mousePosition.y - py);
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && evt.button == 0 && _isDragging)
        {
            _isDragging = false;
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && _isDragging)
        {
            _panelX = evt.mousePosition.x - _dragOffset.x;
            _panelY = evt.mousePosition.y - _dragOffset.y;
            evt.Use();
        }

        // ── Consume all mouse events inside the panel so nothing clicks through ──
        if (evt.isMouse && panelRect.Contains(evt.mousePosition))
        {
            // Don't Use() the event here - let our own controls process it first.
            // But for any unhandled mouse-down, mark it used at the end.
        }

        float pad = 16f;
        float sidebarWidth = 150f;
        float titleHeight = 40f;
        float closeHeight = 36f;
        float bottomPad = 12f;

        GUI.DrawTexture(panelRect, _windowBg);

        if (GUI.Button(new Rect(px + w - 38f, py + 6f, 30f, 30f), "X", _buttonStyle))
            HidePanel();

        GUI.Label(new Rect(px + pad, py + pad, w - pad * 2 - 35f, 30f), "MOD SETTINGS", _titleStyle);

        float contentTop = py + pad + titleHeight;
        float contentBottom = py + h - bottomPad - closeHeight - bottomPad;
        float contentHeight = contentBottom - contentTop;

        var sidebarRect = new Rect(px, contentTop, sidebarWidth + pad, contentHeight);
        GUI.DrawTexture(sidebarRect, _sidebarBg);

        // ── Left sidebar: mod list ──
        float sideY = contentTop + 4f;
        float sideX = px + 6f;
        float sideBtnW = sidebarWidth + pad - 12f;
        float sideBtnH = 32f;

        if (_modOrder.Count == 0)
        {
            GUI.Label(new Rect(sideX, sideY, sideBtnW, 24f), "No mods", _descriptionStyle);
        }
        else
        {
            foreach (var modId in _modOrder)
            {
                if (sideY + sideBtnH > contentBottom) break;

                bool isSelected = modId == _selectedModId;
                var style = isSelected ? _modListSelectedStyle : _modListButtonStyle;

                string displayName = modId;
                if (displayName.Length > 16) displayName = displayName.Substring(0, 14) + "..";

                if (GUI.Button(new Rect(sideX, sideY, sideBtnW, sideBtnH), displayName, style))
                {
                    _selectedModId = modId;
                    _scrollOffset = 0f;
                }

                sideY += sideBtnH + 3f;
            }
        }

        // ── Right content area ──
        float rightX = px + sidebarWidth + pad + 8f;
        float rightW = w - sidebarWidth - pad - 8f - pad;
        float rightTop = contentTop + 4f;
        float rightBottom = contentBottom - 4f;

        if (_selectedModId != null && _mods.TryGetValue(_selectedModId, out var selectedMod))
        {
            DrawModEntries(selectedMod, rightX, rightTop, rightW, rightBottom);
        }
        else if (_modOrder.Count > 0)
        {
            GUI.Label(new Rect(rightX, rightTop, rightW, 24f), "Select a mod from the list.", _labelStyle);
        }
        else
        {
            GUI.Label(new Rect(rightX, rightTop, rightW, 48f),
                "No mods have registered config entries.\n\nPress F8 to toggle this panel.",
                _labelStyle);
        }


        float creditsH = 16f;
        float creditsY = py + h - bottomPad - creditsH;
        var prevAlign = _rangeLabelStyle.alignment;
        var prevColor = _rangeLabelStyle.normal.textColor;
        _rangeLabelStyle.alignment = TextAnchor.MiddleCenter;
        _rangeLabelStyle.normal.textColor = new Color(0.75f, 0.85f, 0.85f, 0.9f);
        GUI.Label(new Rect(px + pad, creditsY, w - pad * 2, creditsH), "RustBridge by Joniii", _rangeLabelStyle);
        _rangeLabelStyle.alignment = prevAlign;
        _rangeLabelStyle.normal.textColor = prevColor;

        float closeBtnW = 120f;
        float closeBtnX = px + (w - closeBtnW) / 2f;
        float closeBtnY = creditsY - closeHeight - 6f;
        if (GUI.Button(new Rect(closeBtnX, closeBtnY, closeBtnW, closeHeight), "Close", _closeButtonStyle))
            HidePanel();

        if (evt.type == EventType.MouseDown && evt.button == 0 && !panelRect.Contains(evt.mousePosition))
        {
            HidePanel();
            evt.Use();
        }
    }

    private static void DrawModEntries(ModConfig mod, float x, float top, float width, float bottom)
    {
        float y = top - _scrollOffset;
        float visibleTop = top;
        float visibleBottom = bottom;

        GUI.Label(new Rect(x, y, width, 28f), mod.ModId, _titleStyle);
        y += 30f;

        string metaLine = "";
        if (!string.IsNullOrEmpty(mod.Version)) metaLine += "v" + mod.Version;
        if (!string.IsNullOrEmpty(mod.Author))
        {
            if (metaLine.Length > 0) metaLine += "  ·  ";
            metaLine += "by " + mod.Author;
        }
        if (metaLine.Length > 0)
        {
            GUI.Label(new Rect(x, y, width, 20f), metaLine, _descriptionStyle);
            y += 22f;
        }

        GUI.DrawTexture(new Rect(x, y, width, 1f), _fieldBg);
        y += 8f;

        if (mod.EntryOrder.Count == 0)
        {
            GUI.Label(new Rect(x, y + 8f, width, 40f), "No configurable options available.", _descriptionStyle);
            return;
        }

        float entryStartY = y;

        foreach (var key in mod.EntryOrder)
        {
            if (!mod.Entries.TryGetValue(key, out var entry))
                continue;

            switch (entry.Type)
            {
                case ConfigEntryType.Bool:
                    DrawBoolEntry(mod, entry, x, ref y, width);
                    break;
                case ConfigEntryType.Int:
                    DrawIntEntry(mod, entry, x, ref y, width);
                    break;
                case ConfigEntryType.Float:
                    DrawFloatEntry(mod, entry, x, ref y, width);
                    break;
            }

            y += 6f; // spacing between entries
        }

        float totalContentHeight = y - entryStartY + _scrollOffset;
        float viewHeight = visibleBottom - visibleTop - 42f; // minus header

        if (totalContentHeight > viewHeight)
        {
            var contentRect = new Rect(x, visibleTop, width, visibleBottom - visibleTop);
            if (contentRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.ScrollWheel)
                {
                    _scrollOffset += Event.current.delta.y * 20f;
                    _scrollOffset = Mathf.Clamp(_scrollOffset, 0f, totalContentHeight - viewHeight);
                    Event.current.Use();
                }
            }

            _scrollOffset = Mathf.Clamp(_scrollOffset, 0f, Mathf.Max(0f, totalContentHeight - viewHeight));
        }
        else
        {
            _scrollOffset = 0f;
        }
    }

    private static void DrawBoolEntry(ModConfig mod, ConfigEntry entry, float x, ref float y, float width)
    {
        GUI.Label(new Rect(x, y, width - 70f, 22f), entry.DisplayName, _labelStyle);

        float toggleW = 56f;
        float toggleH = 24f;
        float toggleX = x + width - toggleW;

        string toggleLabel = entry.BoolValue ? "ON" : "OFF";
        var toggleStyle = entry.BoolValue ? _toggleOnStyle : _toggleOffStyle;

        if (GUI.Button(new Rect(toggleX, y, toggleW, toggleH), toggleLabel, toggleStyle))
        {
            entry.BoolValue = !entry.BoolValue;
            SaveModConfig(mod);
            CrashLog.Log($"ModConfig: toggled '{mod.ModId}/{entry.Key}' = {entry.BoolValue}");
        }

        y += 26f;

        if (!string.IsNullOrEmpty(entry.Description))
        {
            GUI.Label(new Rect(x + 4f, y, width - 4f, 18f), entry.Description, _descriptionStyle);
            y += 20f;
        }
    }

    private static void DrawIntEntry(ModConfig mod, ConfigEntry entry, float x, ref float y, float width)
    {
        GUI.Label(new Rect(x, y, width, 22f), entry.DisplayName, _labelStyle);
        y += 24f;

        float btnW = 32f;
        float btnH = 26f;
        float valueW = 60f;
        float rangeW = 100f;

        float cx = x;

        if (GUI.Button(new Rect(cx, y, btnW, btnH), "-", _smallButtonStyle))
        {
            int step = IsShiftHeld() ? 10 : 1;
            entry.IntValue = Math.Max(entry.IntMin, entry.IntValue - step);
            SaveModConfig(mod);
            CrashLog.Log($"ModConfig: adjusted '{mod.ModId}/{entry.Key}' = {entry.IntValue}");
        }
        cx += btnW + 4f;

        GUI.Label(new Rect(cx, y, valueW, btnH), entry.IntValue.ToString(), _valueStyle);
        cx += valueW + 4f;

        if (GUI.Button(new Rect(cx, y, btnW, btnH), "+", _smallButtonStyle))
        {
            int step = IsShiftHeld() ? 10 : 1;
            entry.IntValue = Math.Min(entry.IntMax, entry.IntValue + step);
            SaveModConfig(mod);
            CrashLog.Log($"ModConfig: adjusted '{mod.ModId}/{entry.Key}' = {entry.IntValue}");
        }
        cx += btnW + 10f;

        GUI.Label(new Rect(cx, y, rangeW, btnH), $"{entry.IntMin} - {entry.IntMax}", _rangeLabelStyle);

        y += btnH + 2f;

        if (!string.IsNullOrEmpty(entry.Description))
        {
            GUI.Label(new Rect(x + 4f, y, width - 4f, 18f), entry.Description, _descriptionStyle);
            y += 20f;
        }
    }

    private static void DrawFloatEntry(ModConfig mod, ConfigEntry entry, float x, ref float y, float width)
    {
        GUI.Label(new Rect(x, y, width, 22f), entry.DisplayName, _labelStyle);
        y += 24f;

        float btnW = 32f;
        float btnH = 26f;
        float valueW = 70f;
        float rangeW = 120f;

        float cx = x;

        if (GUI.Button(new Rect(cx, y, btnW, btnH), "-", _smallButtonStyle))
        {
            float step = IsShiftHeld() ? 1.0f : 0.1f;
            entry.FloatValue = Math.Max(entry.FloatMin, entry.FloatValue - step);
            entry.FloatValue = (float)Math.Round(entry.FloatValue, 1);
            SaveModConfig(mod);
            CrashLog.Log($"ModConfig: adjusted '{mod.ModId}/{entry.Key}' = {entry.FloatValue:F1}");
        }
        cx += btnW + 4f;

        GUI.Label(new Rect(cx, y, valueW, btnH), entry.FloatValue.ToString("F1"), _valueStyle);
        cx += valueW + 4f;

        if (GUI.Button(new Rect(cx, y, btnW, btnH), "+", _smallButtonStyle))
        {
            float step = IsShiftHeld() ? 1.0f : 0.1f;
            entry.FloatValue = Math.Min(entry.FloatMax, entry.FloatValue + step);
            entry.FloatValue = (float)Math.Round(entry.FloatValue, 1);
            SaveModConfig(mod);
            CrashLog.Log($"ModConfig: adjusted '{mod.ModId}/{entry.Key}' = {entry.FloatValue:F1}");
        }
        cx += btnW + 10f;

        GUI.Label(new Rect(cx, y, rangeW, btnH), $"{entry.FloatMin:F1} - {entry.FloatMax:F1}", _rangeLabelStyle);

        y += btnH + 2f;

        if (!string.IsNullOrEmpty(entry.Description))
        {
            GUI.Label(new Rect(x + 4f, y, width - 4f, 18f), entry.Description, _descriptionStyle);
            y += 20f;
        }
    }

    private static void InitStyles()
    {
        var defaultFont = GUI.skin.font;

        // Textures
        _windowBg = MakeTex(1, 1, new Color(40f / 255f, 40f / 255f, 40f / 255f, 240f / 255f));
        _buttonBg = MakeTex(1, 1, new Color(0f, 180f / 255f, 180f / 255f, 1f));
        _buttonHoverBg = MakeTex(1, 1, new Color(0f, 210f / 255f, 210f / 255f, 1f));
        _fieldBg = MakeTex(1, 1, new Color(60f / 255f, 60f / 255f, 60f / 255f, 1f));
        _toggleOnBg = MakeTex(1, 1, new Color(0f, 160f / 255f, 0f, 1f));
        _toggleOnHoverBg = MakeTex(1, 1, new Color(0f, 190f / 255f, 0f, 1f));
        _toggleOffBg = MakeTex(1, 1, new Color(80f / 255f, 80f / 255f, 80f / 255f, 1f));
        _toggleOffHoverBg = MakeTex(1, 1, new Color(100f / 255f, 100f / 255f, 100f / 255f, 1f));
        _modListBg = MakeTex(1, 1, new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f));
        _modListHoverBg = MakeTex(1, 1, new Color(70f / 255f, 70f / 255f, 70f / 255f, 1f));
        _modListSelectedBg = MakeTex(1, 1, new Color(0f, 140f / 255f, 140f / 255f, 1f));
        _sidebarBg = MakeTex(1, 1, new Color(30f / 255f, 30f / 255f, 30f / 255f, 200f / 255f));

        // ── Title style ──
        _titleStyle = new GUIStyle();
        _titleStyle.font = defaultFont;
        _titleStyle.fontSize = 20;
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.normal.textColor = Color.white;
        _titleStyle.alignment = TextAnchor.MiddleLeft;
        _titleStyle.margin = new RectOffset();
        _titleStyle.padding = new RectOffset();

        // ── Label style ──
        _labelStyle = new GUIStyle();
        _labelStyle.font = defaultFont;
        _labelStyle.fontSize = 14;
        _labelStyle.normal.textColor = Color.white;
        _labelStyle.wordWrap = true;
        _labelStyle.padding = new RectOffset();
        _labelStyle.padding.left = 2;
        _labelStyle.padding.right = 2;

        // ── Description style (smaller, gray) ──
        _descriptionStyle = new GUIStyle();
        _descriptionStyle.font = defaultFont;
        _descriptionStyle.fontSize = 12;
        _descriptionStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        _descriptionStyle.wordWrap = true;
        _descriptionStyle.padding = new RectOffset();
        _descriptionStyle.padding.left = 2;
        _descriptionStyle.padding.right = 2;

        // ── Value display style (centered, monospace-feel) ──
        _valueStyle = new GUIStyle();
        _valueStyle.font = defaultFont;
        _valueStyle.fontSize = 14;
        _valueStyle.fontStyle = FontStyle.Bold;
        _valueStyle.normal.textColor = Color.white;
        _valueStyle.normal.background = _fieldBg;
        _valueStyle.alignment = TextAnchor.MiddleCenter;
        _valueStyle.padding = new RectOffset();
        _valueStyle.padding.left = 4;
        _valueStyle.padding.right = 4;
        _valueStyle.padding.top = 2;
        _valueStyle.padding.bottom = 2;

        // ── Range label style ──
        _rangeLabelStyle = new GUIStyle();
        _rangeLabelStyle.font = defaultFont;
        _rangeLabelStyle.fontSize = 12;
        _rangeLabelStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        _rangeLabelStyle.alignment = TextAnchor.MiddleLeft;
        _rangeLabelStyle.padding = new RectOffset();

        // ── Main button style (teal) ──
        _buttonStyle = new GUIStyle();
        _buttonStyle.font = defaultFont;
        _buttonStyle.fontSize = 14;
        _buttonStyle.fontStyle = FontStyle.Bold;
        _buttonStyle.normal.background = _buttonBg;
        _buttonStyle.normal.textColor = Color.white;
        _buttonStyle.hover.background = _buttonHoverBg;
        _buttonStyle.hover.textColor = Color.white;
        _buttonStyle.active.background = _buttonHoverBg;
        _buttonStyle.active.textColor = Color.white;
        _buttonStyle.focused.background = _buttonBg;
        _buttonStyle.focused.textColor = Color.white;
        _buttonStyle.alignment = TextAnchor.MiddleCenter;
        _buttonStyle.border = new RectOffset();
        _buttonStyle.border.left = 4;
        _buttonStyle.border.right = 4;
        _buttonStyle.border.top = 4;
        _buttonStyle.border.bottom = 4;
        _buttonStyle.margin = new RectOffset();
        _buttonStyle.margin.left = 2;
        _buttonStyle.margin.right = 2;
        _buttonStyle.margin.top = 2;
        _buttonStyle.margin.bottom = 2;
        _buttonStyle.padding = new RectOffset();
        _buttonStyle.padding.left = 8;
        _buttonStyle.padding.right = 8;
        _buttonStyle.padding.top = 4;
        _buttonStyle.padding.bottom = 4;

        // ── Close button style (same as main button) ──
        _closeButtonStyle = new GUIStyle();
        _closeButtonStyle.font = defaultFont;
        _closeButtonStyle.fontSize = 14;
        _closeButtonStyle.fontStyle = FontStyle.Bold;
        _closeButtonStyle.normal.background = _buttonBg;
        _closeButtonStyle.normal.textColor = Color.white;
        _closeButtonStyle.hover.background = _buttonHoverBg;
        _closeButtonStyle.hover.textColor = Color.white;
        _closeButtonStyle.active.background = _buttonHoverBg;
        _closeButtonStyle.active.textColor = Color.white;
        _closeButtonStyle.focused.background = _buttonBg;
        _closeButtonStyle.focused.textColor = Color.white;
        _closeButtonStyle.alignment = TextAnchor.MiddleCenter;
        _closeButtonStyle.border = new RectOffset();
        _closeButtonStyle.border.left = 4;
        _closeButtonStyle.border.right = 4;
        _closeButtonStyle.border.top = 4;
        _closeButtonStyle.border.bottom = 4;
        _closeButtonStyle.padding = new RectOffset();
        _closeButtonStyle.padding.left = 8;
        _closeButtonStyle.padding.right = 8;
        _closeButtonStyle.padding.top = 4;
        _closeButtonStyle.padding.bottom = 4;

        // ── Small button style for [-] [+] ──
        _smallButtonStyle = new GUIStyle();
        _smallButtonStyle.font = defaultFont;
        _smallButtonStyle.fontSize = 14;
        _smallButtonStyle.fontStyle = FontStyle.Bold;
        _smallButtonStyle.normal.background = _buttonBg;
        _smallButtonStyle.normal.textColor = Color.white;
        _smallButtonStyle.hover.background = _buttonHoverBg;
        _smallButtonStyle.hover.textColor = Color.white;
        _smallButtonStyle.active.background = _buttonHoverBg;
        _smallButtonStyle.active.textColor = Color.white;
        _smallButtonStyle.focused.background = _buttonBg;
        _smallButtonStyle.focused.textColor = Color.white;
        _smallButtonStyle.alignment = TextAnchor.MiddleCenter;
        _smallButtonStyle.border = new RectOffset();
        _smallButtonStyle.border.left = 2;
        _smallButtonStyle.border.right = 2;
        _smallButtonStyle.border.top = 2;
        _smallButtonStyle.border.bottom = 2;
        _smallButtonStyle.margin = new RectOffset();
        _smallButtonStyle.padding = new RectOffset();
        _smallButtonStyle.padding.left = 4;
        _smallButtonStyle.padding.right = 4;
        _smallButtonStyle.padding.top = 2;
        _smallButtonStyle.padding.bottom = 2;

        // ── Mod list button style ──
        _modListButtonStyle = new GUIStyle();
        _modListButtonStyle.font = defaultFont;
        _modListButtonStyle.fontSize = 13;
        _modListButtonStyle.normal.background = _modListBg;
        _modListButtonStyle.normal.textColor = Color.white;
        _modListButtonStyle.hover.background = _modListHoverBg;
        _modListButtonStyle.hover.textColor = Color.white;
        _modListButtonStyle.active.background = _modListHoverBg;
        _modListButtonStyle.active.textColor = Color.white;
        _modListButtonStyle.focused.background = _modListBg;
        _modListButtonStyle.focused.textColor = Color.white;
        _modListButtonStyle.alignment = TextAnchor.MiddleLeft;
        _modListButtonStyle.border = new RectOffset();
        _modListButtonStyle.margin = new RectOffset();
        _modListButtonStyle.padding = new RectOffset();
        _modListButtonStyle.padding.left = 8;
        _modListButtonStyle.padding.right = 4;
        _modListButtonStyle.padding.top = 4;
        _modListButtonStyle.padding.bottom = 4;
        _modListButtonStyle.clipping = TextClipping.Clip;

        // ── Mod list selected style (highlighted) ──
        _modListSelectedStyle = new GUIStyle();
        _modListSelectedStyle.font = defaultFont;
        _modListSelectedStyle.fontSize = 13;
        _modListSelectedStyle.fontStyle = FontStyle.Bold;
        _modListSelectedStyle.normal.background = _modListSelectedBg;
        _modListSelectedStyle.normal.textColor = Color.white;
        _modListSelectedStyle.hover.background = _modListSelectedBg;
        _modListSelectedStyle.hover.textColor = Color.white;
        _modListSelectedStyle.active.background = _modListSelectedBg;
        _modListSelectedStyle.active.textColor = Color.white;
        _modListSelectedStyle.focused.background = _modListSelectedBg;
        _modListSelectedStyle.focused.textColor = Color.white;
        _modListSelectedStyle.alignment = TextAnchor.MiddleLeft;
        _modListSelectedStyle.border = new RectOffset();
        _modListSelectedStyle.margin = new RectOffset();
        _modListSelectedStyle.padding = new RectOffset();
        _modListSelectedStyle.padding.left = 8;
        _modListSelectedStyle.padding.right = 4;
        _modListSelectedStyle.padding.top = 4;
        _modListSelectedStyle.padding.bottom = 4;
        _modListSelectedStyle.clipping = TextClipping.Clip;

        // ── Toggle ON style (green-teal) ──
        _toggleOnStyle = new GUIStyle();
        _toggleOnStyle.font = defaultFont;
        _toggleOnStyle.fontSize = 13;
        _toggleOnStyle.fontStyle = FontStyle.Bold;
        _toggleOnStyle.normal.background = _toggleOnBg;
        _toggleOnStyle.normal.textColor = Color.white;
        _toggleOnStyle.hover.background = _toggleOnHoverBg;
        _toggleOnStyle.hover.textColor = Color.white;
        _toggleOnStyle.active.background = _toggleOnHoverBg;
        _toggleOnStyle.active.textColor = Color.white;
        _toggleOnStyle.focused.background = _toggleOnBg;
        _toggleOnStyle.focused.textColor = Color.white;
        _toggleOnStyle.alignment = TextAnchor.MiddleCenter;
        _toggleOnStyle.border = new RectOffset();
        _toggleOnStyle.margin = new RectOffset();
        _toggleOnStyle.padding = new RectOffset();
        _toggleOnStyle.padding.left = 4;
        _toggleOnStyle.padding.right = 4;
        _toggleOnStyle.padding.top = 2;
        _toggleOnStyle.padding.bottom = 2;

        // ── Toggle OFF style (dark gray) ──
        _toggleOffStyle = new GUIStyle();
        _toggleOffStyle.font = defaultFont;
        _toggleOffStyle.fontSize = 13;
        _toggleOffStyle.fontStyle = FontStyle.Bold;
        _toggleOffStyle.normal.background = _toggleOffBg;
        _toggleOffStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        _toggleOffStyle.hover.background = _toggleOffHoverBg;
        _toggleOffStyle.hover.textColor = Color.white;
        _toggleOffStyle.active.background = _toggleOffHoverBg;
        _toggleOffStyle.active.textColor = Color.white;
        _toggleOffStyle.focused.background = _toggleOffBg;
        _toggleOffStyle.focused.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        _toggleOffStyle.alignment = TextAnchor.MiddleCenter;
        _toggleOffStyle.border = new RectOffset();
        _toggleOffStyle.margin = new RectOffset();
        _toggleOffStyle.padding = new RectOffset();
        _toggleOffStyle.padding.left = 4;
        _toggleOffStyle.padding.right = 4;
        _toggleOffStyle.padding.top = 2;
        _toggleOffStyle.padding.bottom = 2;

        _stylesInitialized = true;
        CrashLog.Log("ModConfig: IMGUI styles initialized.");
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var tex = new Texture2D(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, col);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}
