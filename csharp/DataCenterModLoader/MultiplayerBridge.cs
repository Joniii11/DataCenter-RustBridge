using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;

namespace DataCenterModLoader;

/// <summary>
/// Manages the multiplayer bridge between C# (MelonLoader) and the Rust DLL (dc_multiplayer.dll).
/// Handles relay-based networking, remote player rendering, UI panel, and main menu button injection.
/// </summary>
public class MultiplayerBridge
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // ═══════════════════════════════════════════════════════════════════════
    //  FFI Delegates (dc_multiplayer.dll exports)
    // ═══════════════════════════════════════════════════════════════════════

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpGetRemotePlayersDelegate(IntPtr buf, uint maxCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpIsConnectedDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpIsRelayActiveDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpGetPlayerCountDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong MpGetMySteamIdDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpHostDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpConnectDelegate(IntPtr roomCode, uint roomCodeLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpDisconnectDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MpGetRoomCodeDelegate();

    // ═══════════════════════════════════════════════════════════════════════
    //  Structs & Inner Types
    // ═══════════════════════════════════════════════════════════════════════

    // Must match crates/dc_multiplayer RemotePlayerData #[repr(C)] (align 8, ~96 bytes with tail padding).
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct RemotePlayerData
    {
        public ulong SteamId;
        public float X, Y, Z, RotY;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Name;
        public byte Connected;
    }

    private class RemotePlayerGO
    {
        public GameObject GO;
        public ulong SteamId;
        public Vector3 TargetPos;
        public float TargetRotY;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Fields: DLL / FFI
    // ═══════════════════════════════════════════════════════════════════════

    private readonly MelonLogger.Instance _logger;
    private MpGetRemotePlayersDelegate _getRemotePlayers;
    private MpIsConnectedDelegate _isConnected;
    private MpIsRelayActiveDelegate _isRelayActive;
    private MpGetPlayerCountDelegate _getPlayerCount;
    private MpGetMySteamIdDelegate _getMySteamId;
    private MpHostDelegate _host;
    private MpConnectDelegate _connect;
    private MpDisconnectDelegate _disconnect;
    private MpGetRoomCodeDelegate _getRoomCode;
    private readonly Dictionary<ulong, RemotePlayerGO> _remotePlayers = new();
    private bool _initialized = false;
    private float _initTimer = 0f;
    private bool _isHosting = false;
    private bool _isConnectedState = false;

    // ═══════════════════════════════════════════════════════════════════════
    //  Fields: Relay / Room Code
    // ═══════════════════════════════════════════════════════════════════════

    private string _roomCode = "";  // room code for joining
    private string _displayRoomCode = "";  // room code received after hosting

    // ═══════════════════════════════════════════════════════════════════════
    //  Fields: UI
    // ═══════════════════════════════════════════════════════════════════════

    private bool _showPanel;
    private bool _pendingMenuInjection;
    private float _menuInjectionTimer;
    private GameObject _menuButton;
    private Rect _panelRect;
    private bool _stylesInitialized;
    private GUIStyle _windowStyle, _buttonStyle, _labelStyle, _textFieldStyle, _titleStyle, _statusStyle, _stopHostButtonStyle, _fieldFocusedStyle;
    private Texture2D _windowBg, _buttonBg, _buttonHoverBg, _fieldBg, _stopBtnBg, _stopBtnHoverBg, _fieldActiveBg;

    // Custom text field state (GUI.TextField doesn't work with new Input System)
    private bool _roomCodeFieldFocused;
    private float _cursorBlinkTimer;
    private bool _cursorVisible = true;
    private float _keyRepeatTimer;
    private Key _lastHeldKey = Key.None;
    private const float KEY_REPEAT_DELAY = 0.4f;
    private const float KEY_REPEAT_RATE = 0.05f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Constants
    // ═══════════════════════════════════════════════════════════════════════

    private const int MAX_REMOTE_PLAYERS = 4;

    // ═══════════════════════════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════════════════════════

    public MultiplayerBridge(MelonLogger.Instance logger)
    {
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DLL Detection & Initialization
    // ═══════════════════════════════════════════════════════════════════════

    public bool TryInitialize()
    {
        if (_initialized) return true;

        // Match how Windows registers the module (with or without .dll).
        var handle = GetModuleHandle("dc_multiplayer.dll");
        if (handle == IntPtr.Zero)
            handle = GetModuleHandle("dc_multiplayer");
        if (handle == IntPtr.Zero) return false;

        var getPlayersPtr = GetProcAddress(handle, "mp_get_remote_players");
        var isConnectedPtr = GetProcAddress(handle, "mp_is_connected");
        var isRelayActivePtr = GetProcAddress(handle, "mp_is_relay_active");
        var playerCountPtr = GetProcAddress(handle, "mp_get_player_count");
        var steamIdPtr = GetProcAddress(handle, "mp_get_my_steam_id");
        var hostPtr = GetProcAddress(handle, "mp_host");
        var connectPtr = GetProcAddress(handle, "mp_connect");
        var disconnectPtr = GetProcAddress(handle, "mp_disconnect");
        var roomCodePtr = GetProcAddress(handle, "mp_get_room_code");

        if (getPlayersPtr == IntPtr.Zero || isConnectedPtr == IntPtr.Zero) return false;

        _getRemotePlayers = Marshal.GetDelegateForFunctionPointer<MpGetRemotePlayersDelegate>(getPlayersPtr);
        _isConnected = Marshal.GetDelegateForFunctionPointer<MpIsConnectedDelegate>(isConnectedPtr);
        _isRelayActive = isRelayActivePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpIsRelayActiveDelegate>(isRelayActivePtr) : null;
        _getPlayerCount = playerCountPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetPlayerCountDelegate>(playerCountPtr) : null;
        _getMySteamId = steamIdPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetMySteamIdDelegate>(steamIdPtr) : null;
        _host = hostPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpHostDelegate>(hostPtr) : null;
        _connect = connectPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpConnectDelegate>(connectPtr) : null;
        _disconnect = disconnectPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpDisconnectDelegate>(disconnectPtr) : null;
        _getRoomCode = roomCodePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetRoomCodeDelegate>(roomCodePtr) : null;

        _initialized = true;
        _logger.Msg("[MP Bridge] dc_multiplayer detected, bridge active.");
        _logger.Msg("[MP Bridge] Keybinds: F9=Host, F10=Multiplayer Panel, F11=Disconnect");

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OnUpdate  (called every frame from Core.OnUpdate)
    // ═══════════════════════════════════════════════════════════════════════

    public void OnUpdate(float dt)
    {
        // --- Handle pending main-menu button injection ---
        if (_pendingMenuInjection)
        {
            _menuInjectionTimer -= dt;
            if (_menuInjectionTimer <= 0f)
            {
                _pendingMenuInjection = false;
                InjectMainMenuButton();
            }
        }

        // --- Retry DLL detection until initialized ---
        if (!_initialized)
        {
            _initTimer += dt;
            if (_initTimer >= 2f)
            {
                _initTimer = 0f;
                if (TryInitialize())
                {
                    CrashLog.Log("[MP Bridge] dc_multiplayer.dll detected and initialized.");
                }
                else
                {
                    CrashLog.Log("[MP Bridge] dc_multiplayer.dll not found yet, will retry...");
                }
            }

            // Give feedback if the user presses keybinds before DLL is loaded
            var kb = Keyboard.current;
            if (kb != null && (kb.f9Key.wasPressedThisFrame || kb.f10Key.wasPressedThisFrame || kb.f11Key.wasPressedThisFrame))
            {
                _logger.Warning("[MP Bridge] dc_multiplayer.dll is not loaded — multiplayer keybinds (F9/F10/F11) are unavailable.");
                _logger.Warning("[MP Bridge] Make sure dc_multiplayer.dll is in your Mods/native folder and has been loaded.");

                try
                {
                    var ui = StaticUIElements.instance;
                    if (ui != null)
                        ui.AddMeesageInField("Multiplayer: dc_multiplayer.dll not loaded! Check Mods/native folder.");
                }
                catch { }
            }

            return;
        }

        // --- Main update loop (only when initialized) ---
        try
        {
            HandleKeybinds();

            // Check for room code when hosting and we don't have one yet
            if (_isHosting && string.IsNullOrEmpty(_displayRoomCode) && _getRoomCode != null)
            {
                IntPtr codePtr = _getRoomCode();
                if (codePtr != IntPtr.Zero)
                {
                    _displayRoomCode = Marshal.PtrToStringAnsi(codePtr);
                    if (!string.IsNullOrEmpty(_displayRoomCode))

                    {
                        CrashLog.Log($"[MP Bridge] Room code: {_displayRoomCode}");
                        _logger.Msg($"[MP Bridge] Room code: {_displayRoomCode}");
                        try
                        {
                            var ui = StaticUIElements.instance;
                            if (ui != null) ui.AddMeesageInField($"Multiplayer: Room code: {_displayRoomCode}");
                        }
                        catch { }
                    }
                }
            }

            bool connected = _isConnected() != 0;

            // Log state transitions and show in-game notifications
            if (connected && !_isConnectedState)
            {
                _isConnectedState = true;
                _logger.Msg("[MP Bridge] Connected! Remote players will now be rendered.");
                try
                {
                    uint playerCount = _getPlayerCount != null ? _getPlayerCount() : 0;
                    var ui = StaticUIElements.instance;
                    if (ui != null)
                    {
                        if (_isHosting)
                            ui.AddMeesageInField($"Multiplayer: A player connected! ({playerCount} player(s) in session)");
                        else
                            ui.AddMeesageInField("Multiplayer: Connected to host!");
                    }
                }
                catch { }
            }
            else if (!connected && _isConnectedState)
            {
                _isConnectedState = false;
                _logger.Msg("[MP Bridge] Disconnected.");
                try
                {
                    var ui = StaticUIElements.instance;
                    if (ui != null)
                        ui.AddMeesageInField("Multiplayer: Player disconnected.");
                }
                catch { }
            }


            bool relayAlive = _isRelayActive != null ? _isRelayActive() != 0 : connected;

            if (!relayAlive && (_isHosting || _isConnectedState))
            {
                // Only reset once on transition
                if (_isHosting)
                {
                    _isHosting = false;
                    _displayRoomCode = "";
                    _logger.Msg("[MP Bridge] Relay disconnected while hosting, state reset.");
                }
                if (_isConnectedState)
                {
                    _isConnectedState = false;
                    _logger.Msg("[MP Bridge] Relay disconnected while connected, state reset.");
                }
            }

            if (!connected)
            {
                CleanupAll();
                return;
            }

            UpdateRemotePlayers();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("MultiplayerBridge.OnUpdate", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Scene Management
    // ═══════════════════════════════════════════════════════════════════════

    public void OnSceneLoaded(string sceneName)
    {
        if (sceneName == "MainMenu")
        {
            _pendingMenuInjection = true;
            _menuInjectionTimer = 0.5f;
        }
        else
        {
            // Clean up menu button reference on scene change
            _menuButton = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Keybinds
    // ═══════════════════════════════════════════════════════════════════════

    private void HandleKeybinds()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // F9 = Host game
        if (kb.f9Key.wasPressedThisFrame)
        {
            DoHost();
        }

        // F10 = Toggle multiplayer panel
        if (kb.f10Key.wasPressedThisFrame)
        {
            if (_showPanel)
                HideMultiplayerPanel();
            else
                ShowMultiplayerPanel();
        }

        // F11 = Disconnect
        if (kb.f11Key.wasPressedThisFrame)
        {
            DoDisconnect();
        }

        // Handle custom text field input when focused
        if (_showPanel && _roomCodeFieldFocused)
        {
            HandleTextFieldInput(kb);
        }
    }

    /// <summary>
    /// Manually handles keyboard input for the room code text field since GUI.TextField
    /// doesn't work when the game uses the new Input System exclusively.
    /// </summary>
    private void HandleTextFieldInput(Keyboard kb)
    {
        bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;

        int maxLen = 16;

        // Ctrl+V = Paste
        if (ctrl && kb.vKey.wasPressedThisFrame)
        {
            string clip = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip))
            {
                // Room codes: alphanumeric only, uppercase
                var filtered = new System.Text.StringBuilder();
                foreach (char c in clip)
                {
                    if (char.IsLetterOrDigit(c)) filtered.Append(char.ToUpper(c));
                }
                _roomCode = (_roomCode ?? "") + filtered.ToString();
                if (_roomCode.Length > maxLen) _roomCode = _roomCode.Substring(0, maxLen);
            }
            return;
        }

        // Ctrl+A = Select all (clear for simplicity)
        if (ctrl && kb.aKey.wasPressedThisFrame)
        {
            _roomCode = "";
            return;
        }

        // Escape = unfocus
        if (kb.escapeKey.wasPressedThisFrame)
        {
            _roomCodeFieldFocused = false;
            return;
        }

        // Enter = trigger join
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            _roomCodeFieldFocused = false;
            DoConnect();
            return;
        }

        // Room code field: alphanumeric, auto-uppercase
        var alphaKeys = new (Key key, char ch)[]
        {
            (Key.A, 'A'), (Key.B, 'B'), (Key.C, 'C'), (Key.D, 'D'),
            (Key.E, 'E'), (Key.F, 'F'), (Key.G, 'G'), (Key.H, 'H'),
            (Key.I, 'I'), (Key.J, 'J'), (Key.K, 'K'), (Key.L, 'L'),
            (Key.M, 'M'), (Key.N, 'N'), (Key.O, 'O'), (Key.P, 'P'),
            (Key.Q, 'Q'), (Key.R, 'R'), (Key.S, 'S'), (Key.T, 'T'),
            (Key.U, 'U'), (Key.V, 'V'), (Key.W, 'W'), (Key.X, 'X'),
            (Key.Y, 'Y'), (Key.Z, 'Z'),
            (Key.Digit0, '0'), (Key.Digit1, '1'), (Key.Digit2, '2'),
            (Key.Digit3, '3'), (Key.Digit4, '4'), (Key.Digit5, '5'),
            (Key.Digit6, '6'), (Key.Digit7, '7'), (Key.Digit8, '8'),
            (Key.Digit9, '9'),
            (Key.Numpad0, '0'), (Key.Numpad1, '1'), (Key.Numpad2, '2'),
            (Key.Numpad3, '3'), (Key.Numpad4, '4'), (Key.Numpad5, '5'),
            (Key.Numpad6, '6'), (Key.Numpad7, '7'), (Key.Numpad8, '8'),
            (Key.Numpad9, '9'),
        };

        foreach (var (key, ch) in alphaKeys)
        {
            if (ShouldProcessKey(kb, key))
            {
                if ((_roomCode ?? "").Length < maxLen)
                    _roomCode = (_roomCode ?? "") + ch;
                return;
            }
        }

        // Backspace
        if (ShouldProcessKey(kb, Key.Backspace))
        {
            if (!string.IsNullOrEmpty(_roomCode))
                _roomCode = _roomCode.Substring(0, _roomCode.Length - 1);
            return;
        }

        // Delete = clear all
        if (kb.deleteKey.wasPressedThisFrame)
        {
            _roomCode = "";
            return;
        }
    }

    /// <summary>
    /// Returns true if a key should be processed this frame (initial press or held-repeat).
    /// </summary>
    private bool ShouldProcessKey(Keyboard kb, Key key)
    {
        var control = kb[key];
        if (control.wasPressedThisFrame)
        {
            _lastHeldKey = key;
            _keyRepeatTimer = KEY_REPEAT_DELAY;
            return true;
        }

        if (control.isPressed && _lastHeldKey == key)
        {
            _keyRepeatTimer -= Time.deltaTime;
            if (_keyRepeatTimer <= 0f)
            {
                _keyRepeatTimer = KEY_REPEAT_RATE;
                return true;
            }
        }
        else if (_lastHeldKey == key && !control.isPressed)
        {
            _lastHeldKey = Key.None;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Actions (shared by keybinds and UI buttons)
    // ═══════════════════════════════════════════════════════════════════════

    private void DoHost()
    {
        if (_host == null)
        {
            _logger.Warning("[MP Bridge] mp_host export not available.");
            return;
        }

        if (_isHosting)
        {
            _logger.Msg("[MP Bridge] Already hosting.");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Already hosting!"); } catch { }
            return;
        }

        CrashLog.Log("[MP Bridge] DoHost: calling mp_host()");
        int result = _host();
        CrashLog.Log($"[MP Bridge] DoHost: mp_host returned {result}");

        if (result == 1)
        {
            _isHosting = true;
            _displayRoomCode = "";  // Reset — will be polled in OnUpdate
            _logger.Msg("[MP Bridge] Connecting to relay for hosting...");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Connecting to relay..."); } catch { }
        }
        else
        {
            _logger.Warning("[MP Bridge] Failed to connect to relay server.");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Failed to connect to relay!"); } catch { }
        }
    }

    private void DoConnect()
    {
        if (_connect == null)
        {
            _logger.Warning("[MP Bridge] mp_connect export not available.");
            return;
        }

        string code = _roomCode != null ? _roomCode.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(code))
        {
            _logger.Warning("[MP Bridge] No room code entered.");
            CrashLog.Log("[MP Bridge] DoConnect: empty room code");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Enter a room code!"); } catch { }
            return;
        }

        CrashLog.Log($"[MP Bridge] DoConnect: room={code}");

        byte[] codeBytes = System.Text.Encoding.UTF8.GetBytes(code);
        IntPtr codePtr = Marshal.AllocHGlobal(codeBytes.Length);
        try
        {
            Marshal.Copy(codeBytes, 0, codePtr, codeBytes.Length);
            int result = _connect(codePtr, (uint)codeBytes.Length);
            CrashLog.Log($"[MP Bridge] DoConnect: mp_connect returned {result}");

            if (result == 1)
            {
                _logger.Msg($"[MP Bridge] Joining room {code}...");
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField($"Multiplayer: Joining room {code}..."); } catch { }
            }
            else
            {
                _logger.Warning("[MP Bridge] Failed to connect to relay server.");
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Failed to connect!"); } catch { }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(codePtr);
        }
    }

    private void DoDisconnect()
    {
        if (_disconnect == null)
        {
            _logger.Warning("[MP Bridge] mp_disconnect export not available.");
            return;
        }

        _disconnect();
        _isHosting = false;
        _displayRoomCode = "";
        _logger.Msg("[MP Bridge] Disconnected.");

        try
        {
            var ui = StaticUIElements.instance;
            if (ui != null)
                ui.AddMeesageInField("Multiplayer: Disconnected.");
        }
        catch { }
    }

    private void DoStopHosting()
    {
        if (_disconnect == null)
        {
            _logger.Warning("[MP Bridge] mp_disconnect export not available.");
            return;
        }

        _disconnect();
        _isHosting = false;
        _displayRoomCode = "";
        _logger.Msg("[MP Bridge] Stopped hosting.");

        try
        {
            var ui = StaticUIElements.instance;
            if (ui != null)
                ui.AddMeesageInField("Multiplayer: Stopped hosting.");
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Main Menu Button Injection
    // ═══════════════════════════════════════════════════════════════════════

    private void InjectMainMenuButton()
    {
        try
        {
            // Guard: don't inject twice if the scene reloads
            if (_menuButton != null) return;

            // Find the "Settings" text among all TextMeshProUGUI components (including inactive)
            var allTexts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();

            Transform templateButton = null;

            foreach (var tmp in allTexts)
            {
                if (tmp.text == "Settings")
                {
                    // text.transform.parent = the Button GameObject
                    templateButton = tmp.transform.parent;
                    break;
                }
            }

            if (templateButton == null)
            {
                _logger.Warning("[MP Bridge] Could not find 'Settings' button for menu injection.");
                return;
            }

            // The button GO's parent is the panel/layout that holds all menu buttons
            var buttonPanel = templateButton.parent;
            int siblingIndex = templateButton.GetSiblingIndex();

            // Clone the Settings button into the same panel
            var clone = UnityEngine.Object.Instantiate(templateButton.gameObject, buttonPanel);
            // Place it BEFORE Settings (i.e. after Load Game)
            clone.transform.SetSiblingIndex(siblingIndex);
            clone.name = "MultiplayerButton";

            // ── Step 1: Destroy LocalisedText components ──
            // The game has a custom localisation system (LocalisedText MonoBehaviour)
            // that auto-overrides .text with the localised string every frame/language change.
            // We must destroy it so our "Multiplayer" text sticks.
            var locTexts = clone.GetComponentsInChildren<LocalisedText>(true);
            if (locTexts != null)
            {
                foreach (var lt in locTexts)
                {
                    UnityEngine.Object.Destroy(lt);
                }
                _logger.Msg($"[MP Bridge] Destroyed {locTexts.Count} LocalisedText component(s) on cloned button.");
            }

            // ── Step 2: Change the label text to "Multiplayer" ──
            var cloneTexts = clone.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (cloneTexts != null)
            {
                foreach (var t in cloneTexts)
                {
                    t.text = "Multiplayer";
                    try { t.SetText("Multiplayer"); } catch { }
                    try { t.ForceMeshUpdate(); } catch { }
                }
            }
            _logger.Msg($"[MP Bridge] Found {(cloneTexts != null ? cloneTexts.Count : 0)} TMP component(s) in cloned button.");

            // ── Step 3: Rewire onClick ──
            // The game uses ButtonExtended (extends Selectable), NOT Unity's Button.
            // ButtonExtended has a public onClick property (ButtonClickedEvent) with a setter.
            // The cloned button has a persistent listener pointing to MainMenu.Settings().
            // We replace the entire event to discard it.
            var btnExt = clone.GetComponent<ButtonExtended>();
            if (btnExt != null)
            {
                try
                {
                    btnExt.onClick = new ButtonExtended.ButtonClickedEvent();
                    btnExt.onClick.AddListener((System.Action)(() => ShowMultiplayerPanel()));
                    _logger.Msg("[MP Bridge] Wired ButtonExtended.onClick to ShowMultiplayerPanel.");
                }
                catch (Exception ex2)
                {
                    _logger.Warning($"[MP Bridge] Failed to replace ButtonExtended.onClick: {ex2.Message}");
                    // Fallback: try removing listeners and adding ours
                    try
                    {
                        btnExt.onClick.RemoveAllListeners();
                        btnExt.onClick.AddListener((System.Action)(() => ShowMultiplayerPanel()));
                    }
                    catch { }
                }
            }
            else
            {
                _logger.Warning("[MP Bridge] ButtonExtended not found on clone, trying Unity Button fallback.");
                // Fallback: try standard Unity Button
                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    try
                    {
                        btn.onClick = new Button.ButtonClickedEvent();
                        btn.onClick.AddListener((System.Action)(() => ShowMultiplayerPanel()));
                    }
                    catch { }
                }
            }

            _menuButton = clone;
            _logger.Msg("[MP Bridge] Multiplayer button injected into main menu.");
        }
        catch (Exception ex)
        {
            CrashLog.LogException("InjectMainMenuButton", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  IMGUI Multiplayer Panel
    // ═══════════════════════════════════════════════════════════════════════

    public void ShowMultiplayerPanel()
    {
        _showPanel = true;
    }

    public void HideMultiplayerPanel()
    {
        _showPanel = false;
    }

    /// <summary>
    /// Called from Core.OnGUI(). Draws the multiplayer panel if toggled on.
    /// </summary>
    public void DrawGUI()
    {
        if (!_showPanel) return;

        if (!_stylesInitialized)
            InitStyles();

        // All absolute positioning — GUILayout.* does not work in this IL2CPP context,
        // but GUI.Button / GUI.Label / GUI.TextField with explicit Rect DO work
        // (proven by the X button rendering correctly).

        float w = 400f, h = 560f;
        float px = (Screen.width - w) / 2f;   // panel x
        float py = (Screen.height - h) / 2f;  // panel y
        _panelRect = new Rect(px, py, w, h);

        float pad = 20f;       // inner padding
        float cw = w - pad * 2; // content width
        float cx = px + pad;    // content x
        float y = py + pad;     // running y cursor

        // ── Dark background ──
        GUI.DrawTexture(_panelRect, _windowBg);

        // ── X close button (top-right) ──
        if (GUI.Button(new Rect(px + w - 35f, py + 5f, 30f, 30f), "X", _buttonStyle))
            HideMultiplayerPanel();

        // ── Title ──
        GUI.Label(new Rect(cx, y, cw, 30f), "MULTIPLAYER", _titleStyle);
        y += 40f;

        // ── Steam ID + Copy ──
        ulong myId = (_initialized && _getMySteamId != null) ? _getMySteamId() : 0;
        float copyW = 60f;
        GUI.Label(new Rect(cx, y, cw - copyW - 5f, 24f), $"Your Steam ID: {myId}", _labelStyle);
        if (GUI.Button(new Rect(cx + cw - copyW, y, copyW, 24f), "Copy", _buttonStyle))
        {
            GUIUtility.systemCopyBuffer = myId.ToString();
            _logger.Msg($"[MP Bridge] Steam ID {myId} copied to clipboard.");
        }
        y += 32f;

        // ── Status ──
        string statusText;
        Color statusColor;
        uint playerCount = (_initialized && _getPlayerCount != null) ? _getPlayerCount() : 0;

        if (_isHosting && _isConnectedState)
        {
            statusText = $"Status: HOSTING  ({playerCount} player(s) connected)";
            statusColor = new Color(0f, 1f, 0f); // green — active session
        }
        else if (_isHosting)
        {
            statusText = "Status: HOSTING  (waiting for players...)";
            statusColor = new Color(1f, 1f, 0f); // yellow — hosting but nobody joined
        }
        else if (_isConnectedState)
        {
            statusText = "Status: CONNECTED";
            statusColor = new Color(0f, 1f, 0f); // green
        }
        else
        {
            statusText = "Status: Not Connected";
            statusColor = new Color(1f, 0.3f, 0.3f); // red
        }
        var savedColor = _statusStyle.normal.textColor;
        _statusStyle.normal.textColor = statusColor;
        GUI.Label(new Rect(cx, y, cw, 24f), statusText, _statusStyle);
        _statusStyle.normal.textColor = savedColor;
        y += 30f;

        // ── Connected peer info (shown when connected) ──
        if (_isConnectedState && playerCount > 0)
        {
            _labelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(cx, y, cw, 20f), $"  Players in session: {playerCount}", _labelStyle);
            _labelStyle.normal.textColor = Color.white;
            y += 24f;
        }
        y += 8f;

        // ── Separator: HOST GAME ──
        DrawSectionSeparator(cx, ref y, cw, "HOST GAME");

        // Host / Stop Hosting button
        if (!_initialized) GUI.enabled = false;
        if (_isHosting)
        {
            if (GUI.Button(new Rect(cx, y, cw, 40f), "STOP HOSTING", _stopHostButtonStyle))
                DoStopHosting();
        }
        else
        {
            string hostLabel = _initialized ? "HOST GAME" : "HOST GAME  (waiting...)";
            if (GUI.Button(new Rect(cx, y, cw, 40f), hostLabel, _buttonStyle))
                DoHost();
        }
        GUI.enabled = true;
        y += 48f;

        // Room code display (shown only when hosting and room code available)
        if (_isHosting && !string.IsNullOrEmpty(_displayRoomCode))
        {
            float roomCopyW = 60f;
            GUI.Label(new Rect(cx, y, cw - roomCopyW - 5f, 24f), $"Room Code: {_displayRoomCode}", _labelStyle);
            if (GUI.Button(new Rect(cx + cw - roomCopyW, y, roomCopyW, 24f), "Copy", _buttonStyle))
            {
                GUIUtility.systemCopyBuffer = _displayRoomCode;
                _logger.Msg($"[MP Bridge] Room code {_displayRoomCode} copied to clipboard.");
            }
            y += 32f;
        }

        // ── Separator: JOIN GAME ──
        DrawSectionSeparator(cx, ref y, cw, "JOIN GAME");

        // Room code label
        GUI.Label(new Rect(cx, y, cw, 22f), "Room Code:", _labelStyle);
        y += 26f;

        // Room code text field + paste button
        {
            Rect fieldRect = new Rect(cx, y, cw - 65f, 30f);
            Rect pasteRect = new Rect(cx + cw - 60f, y, 60f, 30f);

            // Toggle focus on click
            if (Event.current.type == EventType.MouseDown)
            {
                if (fieldRect.Contains(Event.current.mousePosition))
                {
                    _roomCodeFieldFocused = true;
                }
                else if (!pasteRect.Contains(Event.current.mousePosition) && !fieldRect.Contains(Event.current.mousePosition))
                {
                    _roomCodeFieldFocused = false;
                }
            }

            var fieldStyle = _roomCodeFieldFocused ? _fieldFocusedStyle : _textFieldStyle;
            string displayText = _roomCode ?? "";

            if (_roomCodeFieldFocused)
            {
                _cursorBlinkTimer += Time.deltaTime;
                if (_cursorBlinkTimer >= 0.5f)
                {
                    _cursorVisible = !_cursorVisible;
                    _cursorBlinkTimer = 0f;
                }
                if (_cursorVisible)
                    displayText += "|";
            }

            if (string.IsNullOrEmpty(_roomCode) && !_roomCodeFieldFocused)
                displayText = "Enter room code...";

            GUI.Label(fieldRect, displayText, fieldStyle);

            // Paste button
            if (GUI.Button(pasteRect, "Paste", _buttonStyle))
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clip))
                {
                    var filtered = new System.Text.StringBuilder();
                    foreach (char c in clip)
                    {
                        if (char.IsLetterOrDigit(c)) filtered.Append(char.ToUpper(c));
                    }
                    if (filtered.Length > 0)
                    {
                        _roomCode = filtered.ToString();
                        if (_roomCode.Length > 16) _roomCode = _roomCode.Substring(0, 16);
                        _logger.Msg($"[MP Bridge] Pasted room code: {_roomCode}");
                    }
                }
                _roomCodeFieldFocused = true;
            }
        }
        y += 38f;

        if (!_initialized) GUI.enabled = false;
        string joinLabel = _initialized ? "JOIN GAME" : "JOIN GAME  (waiting...)";
        if (GUI.Button(new Rect(cx, y, cw, 40f), joinLabel, _buttonStyle))
            DoConnect();
        GUI.enabled = true;
        y += 52f;

        // ── Disconnect (only when connected or hosting) ──
        if (_isHosting || _isConnectedState)
        {
            if (GUI.Button(new Rect(cx, y, cw, 36f), "DISCONNECT", _buttonStyle))
                DoDisconnect();
            y += 44f;
        }

        // ── Unfocus fields when clicking on empty panel area ──
        if (Event.current.type == EventType.MouseDown)
        {
            if (_panelRect.Contains(Event.current.mousePosition))
            {
                // If click is not handled by any field rect above, unfocus all
                // (The field rects set focus above; this catches clicks on the panel background)
                // We use a small trick: schedule unfocus, but the field handlers above already ran
                // so this only fires for non-field clicks
            }
            else
            {
                _roomCodeFieldFocused = false;
            }
        }

        // ── Close at bottom ──
        float closeY = py + h - pad - 32f;
        if (GUI.Button(new Rect(cx, closeY, cw, 32f), "Close", _buttonStyle))
            HideMultiplayerPanel();
    }

    /// <summary>
    /// Draws a labeled section separator line: ─── LABEL ───
    /// </summary>
    private void DrawSectionSeparator(float cx, ref float y, float cw, string label)
    {
        float lineH = 1f;
        float labelW = label.Length * 9f + 16f; // approximate label width
        float lineW = (cw - labelW) / 2f - 4f;

        if (lineW > 0)
        {
            GUI.DrawTexture(new Rect(cx, y + 10f, lineW, lineH), _fieldBg);
            GUI.DrawTexture(new Rect(cx + cw - lineW, y + 10f, lineW, lineH), _fieldBg);
        }

        _labelStyle.alignment = TextAnchor.MiddleCenter;
        _labelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        GUI.Label(new Rect(cx, y, cw, 22f), label, _labelStyle);
        _labelStyle.alignment = TextAnchor.UpperLeft;
        _labelStyle.normal.textColor = Color.white;
        y += 28f;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  IMGUI Style Initialization
    // ═══════════════════════════════════════════════════════════════════════

    private void InitStyles()
    {
        // Grab the default font from GUI.skin — without this, new GUIStyle() has NO font
        // and all text is invisible!
        var defaultFont = GUI.skin.font;

        // Create solid-color textures for backgrounds
        _windowBg = MakeTex(1, 1, new Color(40f / 255f, 40f / 255f, 40f / 255f, 240f / 255f));
        _buttonBg = MakeTex(1, 1, new Color(0f, 180f / 255f, 180f / 255f, 1f));
        _buttonHoverBg = MakeTex(1, 1, new Color(0f, 210f / 255f, 210f / 255f, 1f));
        _fieldBg = MakeTex(1, 1, new Color(60f / 255f, 60f / 255f, 60f / 255f, 1f));
        _fieldActiveBg = MakeTex(1, 1, new Color(80f / 255f, 80f / 255f, 80f / 255f, 1f));
        _stopBtnBg = MakeTex(1, 1, new Color(200f / 255f, 50f / 255f, 50f / 255f, 1f));
        _stopBtnHoverBg = MakeTex(1, 1, new Color(230f / 255f, 70f / 255f, 70f / 255f, 1f));

        _windowStyle = new GUIStyle();
        _windowStyle.normal.background = _windowBg;

        _titleStyle = new GUIStyle();
        _titleStyle.font = defaultFont;
        _titleStyle.fontSize = 20;
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.normal.textColor = Color.white;
        _titleStyle.alignment = TextAnchor.MiddleCenter;
        _titleStyle.margin = new RectOffset();
        _titleStyle.margin.bottom = 10;

        _labelStyle = new GUIStyle();
        _labelStyle.font = defaultFont;
        _labelStyle.fontSize = 14;
        _labelStyle.normal.textColor = Color.white;
        _labelStyle.wordWrap = true;
        _labelStyle.padding = new RectOffset();
        _labelStyle.padding.left = 2; _labelStyle.padding.right = 2;

        _statusStyle = new GUIStyle();
        _statusStyle.font = defaultFont;
        _statusStyle.fontSize = 14;
        _statusStyle.fontStyle = FontStyle.Bold;
        _statusStyle.normal.textColor = Color.white;
        _statusStyle.padding = new RectOffset();
        _statusStyle.padding.left = 2; _statusStyle.padding.right = 2;

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
        _buttonStyle.margin = new RectOffset();
        _buttonStyle.padding = new RectOffset();
        _buttonStyle.border.left = 4; _buttonStyle.border.right = 4;
        _buttonStyle.border.top = 4; _buttonStyle.border.bottom = 4;
        _buttonStyle.margin.left = 2; _buttonStyle.margin.right = 2;
        _buttonStyle.margin.top = 2; _buttonStyle.margin.bottom = 2;
        _buttonStyle.padding.left = 8; _buttonStyle.padding.right = 8;
        _buttonStyle.padding.top = 4; _buttonStyle.padding.bottom = 4;

        // Focused text field style — slightly brighter bg + cyan border feel
        _fieldFocusedStyle = new GUIStyle();
        _fieldFocusedStyle.font = defaultFont;
        _fieldFocusedStyle.fontSize = 14;
        _fieldFocusedStyle.normal.background = _fieldActiveBg;
        _fieldFocusedStyle.normal.textColor = Color.white;
        _fieldFocusedStyle.padding = new RectOffset();
        _fieldFocusedStyle.padding.left = 8; _fieldFocusedStyle.padding.right = 8;
        _fieldFocusedStyle.padding.top = 6; _fieldFocusedStyle.padding.bottom = 6;
        _fieldFocusedStyle.clipping = TextClipping.Clip;

        // Stop hosting button — red background
        _stopHostButtonStyle = new GUIStyle();
        _stopHostButtonStyle.font = defaultFont;
        _stopHostButtonStyle.fontSize = 14;
        _stopHostButtonStyle.fontStyle = FontStyle.Bold;
        _stopHostButtonStyle.normal.background = _stopBtnBg;
        _stopHostButtonStyle.normal.textColor = Color.white;
        _stopHostButtonStyle.hover.background = _stopBtnHoverBg;
        _stopHostButtonStyle.hover.textColor = Color.white;
        _stopHostButtonStyle.active.background = _stopBtnHoverBg;
        _stopHostButtonStyle.active.textColor = Color.white;
        _stopHostButtonStyle.focused.background = _stopBtnBg;
        _stopHostButtonStyle.focused.textColor = Color.white;
        _stopHostButtonStyle.alignment = TextAnchor.MiddleCenter;
        _stopHostButtonStyle.border = new RectOffset();
        _stopHostButtonStyle.margin = new RectOffset();
        _stopHostButtonStyle.padding = new RectOffset();
        _stopHostButtonStyle.border.left = 4; _stopHostButtonStyle.border.right = 4;
        _stopHostButtonStyle.border.top = 4; _stopHostButtonStyle.border.bottom = 4;
        _stopHostButtonStyle.margin.left = 2; _stopHostButtonStyle.margin.right = 2;
        _stopHostButtonStyle.margin.top = 2; _stopHostButtonStyle.margin.bottom = 2;
        _stopHostButtonStyle.padding.left = 8; _stopHostButtonStyle.padding.right = 8;
        _stopHostButtonStyle.padding.top = 4; _stopHostButtonStyle.padding.bottom = 4;

        // Text field: custom drawn since GUI.TextField doesn't work with new Input System
        _textFieldStyle = new GUIStyle();
        _textFieldStyle.font = defaultFont;
        _textFieldStyle.fontSize = 14;
        _textFieldStyle.normal.background = _fieldBg;
        _textFieldStyle.normal.textColor = Color.white;
        _textFieldStyle.focused.background = _fieldBg;
        _textFieldStyle.focused.textColor = Color.white;
        _textFieldStyle.hover.background = _fieldBg;
        _textFieldStyle.hover.textColor = Color.white;
        _textFieldStyle.active.background = _fieldBg;
        _textFieldStyle.active.textColor = Color.white;
        _textFieldStyle.padding = new RectOffset();
        _textFieldStyle.padding.left = 8; _textFieldStyle.padding.right = 8;
        _textFieldStyle.padding.top = 6; _textFieldStyle.padding.bottom = 6;
        _textFieldStyle.clipping = TextClipping.Clip;

        _stylesInitialized = true;
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

    // ═══════════════════════════════════════════════════════════════════════
    //  Remote Player Rendering
    // ═══════════════════════════════════════════════════════════════════════

    private void UpdateRemotePlayers()
    {
        int structSize = Marshal.SizeOf<RemotePlayerData>();
        IntPtr buf = Marshal.AllocHGlobal(structSize * MAX_REMOTE_PLAYERS);

        try
        {
            uint count = _getRemotePlayers(buf, MAX_REMOTE_PLAYERS);

            var activeIds = new HashSet<ulong>();

            for (int i = 0; i < count; i++)
            {
                var data = Marshal.PtrToStructure<RemotePlayerData>(IntPtr.Add(buf, i * structSize));
                if (data.SteamId == 0 || data.Connected == 0) continue;

                activeIds.Add(data.SteamId);

                if (!_remotePlayers.TryGetValue(data.SteamId, out var remote))
                {
                    remote = SpawnRemotePlayer(data);
                    if (remote != null)
                        _remotePlayers[data.SteamId] = remote;
                }

                if (remote != null)
                {
                    remote.TargetPos = new Vector3(data.X, data.Y, data.Z);
                    remote.TargetRotY = data.RotY;

                    // Smooth interpolation towards target position/rotation
                    if (remote.GO != null)
                    {
                        remote.GO.transform.position = Vector3.Lerp(
                            remote.GO.transform.position, remote.TargetPos, Time.deltaTime * 10f);
                        var euler = remote.GO.transform.eulerAngles;
                        euler.y = Mathf.LerpAngle(euler.y, remote.TargetRotY, Time.deltaTime * 10f);
                        remote.GO.transform.eulerAngles = euler;
                    }
                }
            }

            // Destroy players no longer in the list
            var toRemove = new List<ulong>();
            foreach (var kvp in _remotePlayers)
            {
                if (!activeIds.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                if (_remotePlayers.TryGetValue(id, out var old) && old.GO != null)
                    UnityEngine.Object.Destroy(old.GO);
                _remotePlayers.Remove(id);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private RemotePlayerGO SpawnRemotePlayer(RemotePlayerData data)
    {
        try
        {
            // Try to clone a technician prefab for the visual
            var mgr = MainGameManager.instance;
            if (mgr == null || mgr.techniciansPrefabs == null || mgr.techniciansPrefabs.Length == 0)
            {
                _logger.Warning("[MP Bridge] No technician prefabs available, using capsule fallback");
                return SpawnCapsuleFallback(data);
            }

            var prefab = mgr.techniciansPrefabs[0];
            var go = UnityEngine.Object.Instantiate(prefab);

            // Strip AI/pathfinding so it's just a visual
            var nav = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null) UnityEngine.Object.Destroy(nav);

            var tech = go.GetComponent<Technician>();
            if (tech != null) UnityEngine.Object.Destroy(tech);

            go.transform.position = new Vector3(data.X, data.Y, data.Z);
            go.name = $"RemotePlayer_{data.SteamId}";

            string playerName = GetPlayerName(data);
            AddNameTag(go, playerName);

            _logger.Msg($"[MP Bridge] Spawned remote player: {playerName} ({data.SteamId})");

            return new RemotePlayerGO
            {
                GO = go,
                SteamId = data.SteamId,
                TargetPos = new Vector3(data.X, data.Y, data.Z),
                TargetRotY = data.RotY,
            };
        }
        catch (Exception ex)
        {
            CrashLog.LogException("SpawnRemotePlayer", ex);
            return SpawnCapsuleFallback(data);
        }
    }

    private RemotePlayerGO SpawnCapsuleFallback(RemotePlayerData data)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = new Vector3(data.X, data.Y, data.Z);
        go.name = $"RemotePlayer_{data.SteamId}";

        string playerName = GetPlayerName(data);
        AddNameTag(go, playerName);

        return new RemotePlayerGO
        {
            GO = go,
            SteamId = data.SteamId,
            TargetPos = new Vector3(data.X, data.Y, data.Z),
            TargetRotY = data.RotY,
        };
    }

    private string GetPlayerName(RemotePlayerData data)
    {
        if (data.Name != null && data.Name.Length > 0)
        {
            int len = Array.IndexOf(data.Name, (byte)0);
            if (len < 0) len = data.Name.Length;
            if (len > 0) return System.Text.Encoding.UTF8.GetString(data.Name, 0, len);
        }
        return $"Player_{data.SteamId % 10000}";
    }

    private void AddNameTag(GameObject parent, string name)
    {
        // World-space canvas above the player's head
        try
        {
            var canvasGO = new GameObject("NameTag");
            canvasGO.transform.SetParent(parent.transform);
            canvasGO.transform.localPosition = new Vector3(0, 2.2f, 0);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(canvasGO.transform);
            textGO.transform.localPosition = Vector3.zero;

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = name;
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var rect = textGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300, 50);

            // Billboard: always face camera
            canvasGO.AddComponent<BillboardNameTag>();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("AddNameTag", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Cleanup & Shutdown
    // ═══════════════════════════════════════════════════════════════════════

    private void CleanupAll()
    {
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value.GO != null)
                UnityEngine.Object.Destroy(kvp.Value.GO);
        }
        _remotePlayers.Clear();
    }

    public void Shutdown()
    {
        if (_initialized && _disconnect != null)
        {
            try { _disconnect(); }
            catch { }
        }

        CleanupAll();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  BillboardNameTag — always faces the main camera
// ═══════════════════════════════════════════════════════════════════════════

[RegisterTypeInIl2Cpp]
public class BillboardNameTag : MonoBehaviour
{
    public BillboardNameTag(IntPtr ptr) : base(ptr) { }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.LookAt(transform.position + cam.transform.forward);
    }
}
