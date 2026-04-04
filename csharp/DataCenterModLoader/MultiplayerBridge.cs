using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppUMA;
using Il2CppUMA.CharacterSystem;
using UnityEngine.AI;


namespace DataCenterModLoader;

/// <summary>
/// Manages the multiplayer bridge between C# (MelonLoader) and the Rust DLL (dc_multiplayer.dll).
/// Handles relay-based networking, UI panel, and main menu button injection.
/// </summary>
using UnityEngine.SceneManagement;

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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpShouldSendSaveDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpSendSaveDataDelegate(IntPtr data, uint len);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpHasPendingSaveDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpGetSaveDataSizeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpGetSaveDataDelegate(IntPtr buf, uint maxLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpSaveLoadCompleteDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpSkipNextSaveRequestDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpSetLocalSaveHashDelegate(IntPtr data, uint len);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float MpGetSaveTransferProgressDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpGetSaveTransferTotalBytesDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpIsSaveUpToDateDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpGetJoinStateDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpSetJoinStateDelegate(uint state);

    private readonly MelonLogger.Instance _logger;
    private MpIsConnectedDelegate _isConnected;
    private MpIsRelayActiveDelegate _isRelayActive;
    private MpGetPlayerCountDelegate _getPlayerCount;
    private MpGetMySteamIdDelegate _getMySteamId;
    private MpHostDelegate _host;
    private MpConnectDelegate _connect;
    private MpDisconnectDelegate _disconnect;
    private MpGetRoomCodeDelegate _getRoomCode;
    private MpShouldSendSaveDelegate _shouldSendSave;
    private MpSendSaveDataDelegate _sendSaveData;
    private MpHasPendingSaveDelegate _hasPendingSave;
    private MpGetSaveDataSizeDelegate _getSaveDataSize;
    private MpGetSaveDataDelegate _getSaveData;
    private MpSaveLoadCompleteDelegate _saveLoadComplete;
    private MpSkipNextSaveRequestDelegate _skipNextSaveRequest;
    private MpSetLocalSaveHashDelegate _setLocalSaveHash;
    private MpGetSaveTransferProgressDelegate _getSaveTransferProgress;
    private MpGetSaveTransferTotalBytesDelegate _getSaveTransferTotalBytes;
    private MpIsSaveUpToDateDelegate _isSaveUpToDate;
    private MpGetJoinStateDelegate _getJoinState;
    private MpSetJoinStateDelegate _setJoinState;
    private bool _initialized = false;
    private float _initTimer = 0f;
    private bool _isHosting = false;
    private bool _isConnectedState = false;
    private string _discoveredSavePath = null;

    // Join state constants (must match Rust dc_multiplayer JOIN_* constants)
    private const uint JOIN_IDLE = 0;
    private const uint JOIN_WAITING_FOR_SAVE = 1;
    private const uint JOIN_SAVE_READY = 2;
    private const uint JOIN_SAVE_UP_TO_DATE = 3;
    private const uint JOIN_LOADING_SCENE = 4;
    private const uint JOIN_LOADED = 5;
    private string _currentSceneName = "";
    private byte[] _pendingSaveBytes = null;
    private string _pendingSaveName = null;     // save name (without extension) written to disk
    private string _pendingSaveFullPath = null;  // full path to the written save file
    private float _deferredLoadDelay = 0f;       // small delay after scene load before triggering Load()
    private string _reconnectRoomCode = null;    // room code to auto-reconnect after scene transition
    private bool _skipSaveOnReconnect = false;   // skip save processing when reconnecting after load
    private float _reconnectCooldown = 0f;       // cooldown to prevent rapid-fire reconnect attempts
    private bool _gameHandledSaveLoad = false;   // true when MainMenu.Continue() handles the load

    // Host: cached save bytes so multiple client joins don't re-trigger SaveGame()
    private byte[] _cachedSaveData = null;
    private float _cachedSaveAge = 0f;
    private const float SAVE_CACHE_LIFETIME = 30f; // seconds before cache expires

    // ═══════════════════════════════════════════════════════════════════════
    //  Fields: Relay / Room Code
    // ═══════════════════════════════════════════════════════════════════════

    private string _roomCode = "";  // room code for joining
    private string _displayRoomCode = "";  // room code received after hosting

    private bool _showPanel;
    private UnityEngine.EventSystems.EventSystem _mpDisabledEventSystem;
    private int _mpReenableCountdown;
    private bool _pendingMenuInjection;
    private float _menuInjectionTimer;
    private GameObject _menuButton;
    private Rect _panelRect;
    private bool _stylesInitialized;
    private GUIStyle _windowStyle, _buttonStyle, _labelStyle, _textFieldStyle, _titleStyle, _statusStyle, _stopHostButtonStyle, _fieldFocusedStyle;
    private Texture2D _windowBg, _buttonBg, _buttonHoverBg, _fieldBg, _stopBtnBg, _stopBtnHoverBg, _fieldActiveBg;
    private Texture2D _overlayBg;
    private GUIStyle _overlayTextStyle;

    // Custom text field state (GUI.TextField doesn't work with new Input System)
    private bool _roomCodeFieldFocused;
    private float _cursorBlinkTimer;
    private bool _cursorVisible = true;
    private float _keyRepeatTimer;
    private Key _lastHeldKey = Key.None;
    private const float KEY_REPEAT_DELAY = 0.4f;
    private const float KEY_REPEAT_RATE = 0.05f;



    public MultiplayerBridge(MelonLogger.Instance logger)
    {
        _logger = logger;
    }

    public bool TryInitialize()
    {
        if (_initialized) return true;

        // Match how Windows registers the module (with or without .dll).
        var handle = GetModuleHandle("dc_multiplayer.dll");
        if (handle == IntPtr.Zero)
            handle = GetModuleHandle("dc_multiplayer");
        if (handle == IntPtr.Zero) return false;

        var isConnectedPtr = GetProcAddress(handle, "mp_is_connected");
        var isRelayActivePtr = GetProcAddress(handle, "mp_is_relay_active");
        var playerCountPtr = GetProcAddress(handle, "mp_get_player_count");
        var steamIdPtr = GetProcAddress(handle, "mp_get_my_steam_id");
        var hostPtr = GetProcAddress(handle, "mp_host");
        var connectPtr = GetProcAddress(handle, "mp_connect");
        var disconnectPtr = GetProcAddress(handle, "mp_disconnect");
        var roomCodePtr = GetProcAddress(handle, "mp_get_room_code");
        var shouldSendSavePtr = GetProcAddress(handle, "mp_should_send_save");
        var sendSaveDataPtr = GetProcAddress(handle, "mp_send_save_data");
        var hasPendingSavePtr = GetProcAddress(handle, "mp_has_pending_save");
        var getSaveDataSizePtr = GetProcAddress(handle, "mp_get_save_data_size");
        var getSaveDataPtr = GetProcAddress(handle, "mp_get_save_data");
        var saveLoadCompletePtr = GetProcAddress(handle, "mp_save_load_complete");

        if (isConnectedPtr == IntPtr.Zero) return false;

        _isConnected = Marshal.GetDelegateForFunctionPointer<MpIsConnectedDelegate>(isConnectedPtr);
        _isRelayActive = isRelayActivePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpIsRelayActiveDelegate>(isRelayActivePtr) : null;
        _getPlayerCount = playerCountPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetPlayerCountDelegate>(playerCountPtr) : null;
        _getMySteamId = steamIdPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetMySteamIdDelegate>(steamIdPtr) : null;
        _host = hostPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpHostDelegate>(hostPtr) : null;
        _connect = connectPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpConnectDelegate>(connectPtr) : null;
        _disconnect = disconnectPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpDisconnectDelegate>(disconnectPtr) : null;
        _getRoomCode = roomCodePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetRoomCodeDelegate>(roomCodePtr) : null;
        _shouldSendSave = shouldSendSavePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpShouldSendSaveDelegate>(shouldSendSavePtr) : null;
        _sendSaveData = sendSaveDataPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpSendSaveDataDelegate>(sendSaveDataPtr) : null;
        _hasPendingSave = hasPendingSavePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpHasPendingSaveDelegate>(hasPendingSavePtr) : null;
        _getSaveDataSize = getSaveDataSizePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetSaveDataSizeDelegate>(getSaveDataSizePtr) : null;
        _getSaveData = getSaveDataPtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpGetSaveDataDelegate>(getSaveDataPtr) : null;
        _saveLoadComplete = saveLoadCompletePtr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<MpSaveLoadCompleteDelegate>(saveLoadCompletePtr) : null;

        // Optional: may not exist in older DLLs — fail gracefully
        var skipNextSaveRequestPtr = GetProcAddress(handle, "mp_skip_next_save_request");
        _skipNextSaveRequest = skipNextSaveRequestPtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpSkipNextSaveRequestDelegate>(skipNextSaveRequestPtr)
            : null;

        var setLocalSaveHashPtr = GetProcAddress(handle, "mp_set_local_save_hash");
        _setLocalSaveHash = setLocalSaveHashPtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpSetLocalSaveHashDelegate>(setLocalSaveHashPtr)
            : null;

        var getSaveTransferProgressPtr = GetProcAddress(handle, "mp_get_save_transfer_progress");
        _getSaveTransferProgress = getSaveTransferProgressPtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpGetSaveTransferProgressDelegate>(getSaveTransferProgressPtr)
            : null;

        var getSaveTransferTotalBytesPtr = GetProcAddress(handle, "mp_get_save_transfer_total_bytes");
        _getSaveTransferTotalBytes = getSaveTransferTotalBytesPtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpGetSaveTransferTotalBytesDelegate>(getSaveTransferTotalBytesPtr)
            : null;

        var isSaveUpToDatePtr = GetProcAddress(handle, "mp_is_save_up_to_date");
        _isSaveUpToDate = isSaveUpToDatePtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpIsSaveUpToDateDelegate>(isSaveUpToDatePtr)
            : null;

        var getJoinStatePtr = GetProcAddress(handle, "mp_get_join_state");
        _getJoinState = getJoinStatePtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpGetJoinStateDelegate>(getJoinStatePtr)
            : null;

        var setJoinStatePtr = GetProcAddress(handle, "mp_set_join_state");
        _setJoinState = setJoinStatePtr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<MpSetJoinStateDelegate>(setJoinStatePtr)
            : null;

        _initialized = true;
        _logger.Msg("[MP Bridge] dc_multiplayer detected, bridge active.");
        _logger.Msg("[MP Bridge] Keybinds: F9=Host, F10=Multiplayer Panel, F11=Disconnect");

        return true;
    }

    public void OnUpdate(float dt)
    {
        if (_mpReenableCountdown > 0)
        {
            _mpReenableCountdown--;
            if (_mpReenableCountdown <= 0 && _mpDisabledEventSystem != null)
            {
                _mpDisabledEventSystem.enabled = true;
                _mpDisabledEventSystem = null;
            }
        }

        if (_reconnectCooldown > 0f) _reconnectCooldown -= dt;


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

            // Save sync: Host sends save when requested
            if (_isHosting && _shouldSendSave != null && _shouldSendSave() != 0)
            {
                SendSaveToClients();
            }

            if (_isHosting && _cachedSaveData != null)
            {
                _cachedSaveAge += dt;
                if (_cachedSaveAge >= SAVE_CACHE_LIFETIME)
                {
                    _cachedSaveData = null;
                    _cachedSaveAge = 0f;
                    CrashLog.Log("[MP Save] Save cache expired");
                }
            }

            if (!_isHosting)
            {
                uint joinState = GetJoinState();
                switch (joinState)
                {
                    case JOIN_WAITING_FOR_SAVE:
                        // Rust automatically transitions to SaveReady or SaveUpToDate.
                        // Nothing to do here — just wait.
                        break;

                    case JOIN_SAVE_READY:
                        CrashLog.Log("[MP Join] Save data ready from Rust — fetching and processing");
                        try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Received save from host, loading..."); } catch { }
                        FetchAndProcessSave();
                        break;

                    case JOIN_SAVE_UP_TO_DATE:
                        {
                            CrashLog.Log("[MP Join] Save is up to date — no download needed!");
                            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Save is up to date!"); } catch { }

                            string savePath = DiscoverSaveFile();
                            if (savePath != null)
                            {
                                _pendingSaveName = Path.GetFileNameWithoutExtension(savePath);
                                _pendingSaveFullPath = savePath;

                                if (IsInMainMenu())
                                {
                                    CrashLog.Log("[MP Join] In MainMenu with up-to-date save — initiating scene transition");
                                    InitiateSceneTransition();
                                }
                                else
                                {
                                    CrashLog.Log("[MP Join] Ingame with up-to-date save transitioning to Loaded");
                                    SetJoinState(JOIN_LOADED);
                                    _logger.Msg("[MP Join] Save up to date, already ingame!");
                                }
                            }
                            else
                            {
                                CrashLog.Log("[MP Join] Save up to date but couldn't find local file staying in state");
                            }
                            break;
                        }

                    case JOIN_LOADING_SCENE:
                        if (_deferredLoadDelay > 0f)
                        {
                            _deferredLoadDelay -= dt;
                        }
                        else if (!IsInMainMenu())
                        {
                            if (_gameHandledSaveLoad)
                            {
                                CrashLog.Log("[MP Join] Game scene loaded via MainMenu.Continue() — transitioning to Loaded");
                                SetJoinState(JOIN_LOADED);
                                _gameHandledSaveLoad = false;
                                _pendingSaveBytes = null;
                                _pendingSaveName = null;
                                _logger.Msg("[MP Join] Save loaded from host (via game Continue)!");
                                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Save loaded from host!"); } catch { }
                            }
                            else
                            {
                                CrashLog.Log("[MP Join] Game scene detected after deferred wait, attempting load...");
                                AttemptSaveLoad();
                            }
                        }
                        break;

                    case JOIN_LOADED:
                        if (_hasPendingSave != null && _hasPendingSave() != 0)
                        {
                            CrashLog.Log("[MP Join] Discarding late save data (already loaded)");
                            if (_saveLoadComplete != null) _saveLoadComplete();
                        }
                        if (_reconnectRoomCode != null && !relayAlive && _reconnectCooldown <= 0f)
                        {
                            _reconnectCooldown = 5f;
                            CrashLog.Log($"[MP Join] Relay not alive in Loaded state — auto-reconnecting to {_reconnectRoomCode}");
                            AutoReconnect();
                        }
                        break;

                    case JOIN_IDLE:
                    default:
                        break;
                }
            }


        }
        catch (Exception ex)
        {
            CrashLog.LogException("MultiplayerBridge.OnUpdate", ex);
        }
    }


    private uint GetJoinState()
    {
        if (_getJoinState != null) return _getJoinState();
        return JOIN_IDLE;
    }

    private void SetJoinState(uint state)
    {
        if (_setJoinState != null) _setJoinState(state);
    }

    public void OnSceneLoaded(string sceneName)
    {
        _currentSceneName = sceneName ?? "";
        CrashLog.Log($"[MP Join] OnSceneLoaded: \"{_currentSceneName}\" (joinState={GetJoinState()})");

        if (sceneName == "MainMenu")
        {
            _pendingMenuInjection = true;
            _menuInjectionTimer = 0.5f;
        }
        else
        {
            _menuButton = null;

            if (GetJoinState() == JOIN_LOADING_SCENE)
            {
                if (_gameHandledSaveLoad)
                {
                    CrashLog.Log($"[MP Join] Game scene \"{sceneName}\" loaded (via Continue) — waiting for initialization");
                    _deferredLoadDelay = 2.0f;
                }
                else if (_pendingSaveName != null)
                {
                    CrashLog.Log($"[MP Join] Game scene \"{sceneName}\" loaded — will attempt save load after short delay");
                    _deferredLoadDelay = 1.0f;
                }
            }
        }
    }

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

        // Prevent joining when already busy
        uint currentJoinState = GetJoinState();
        if (currentJoinState != JOIN_IDLE && currentJoinState != JOIN_LOADED)
        {
            CrashLog.Log($"[MP Bridge] DoConnect: blocked already in state {currentJoinState}");
            _logger.Msg("[MP Bridge] Already joining, please wait...");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Already joining, please wait..."); } catch { }
            return;
        }

        // Reset join state for a fresh attempt
        ResetJoinState();

        // Compute local save hash for save versioning
        if (_setLocalSaveHash != null)
        {
            try
            {
                string savePath = DiscoverSaveFile();
                if (savePath != null && File.Exists(savePath))
                {
                    byte[] localSave = File.ReadAllBytes(savePath);
                    IntPtr hashPtr = Marshal.AllocHGlobal(localSave.Length);
                    try
                    {
                        Marshal.Copy(localSave, 0, hashPtr, localSave.Length);
                        _setLocalSaveHash(hashPtr, (uint)localSave.Length);
                        CrashLog.Log($"[MP Join] Local save hash computed from {savePath} ({localSave.Length} bytes)");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(hashPtr);
                    }
                }
                else
                {
                    CrashLog.Log("[MP Join] No local save found — hash not set");
                }
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[MP Join] Error computing local save hash: {ex.Message}");
            }
        }

        byte[] codeBytes = System.Text.Encoding.UTF8.GetBytes(code);
        IntPtr codePtr = Marshal.AllocHGlobal(codeBytes.Length);
        try
        {
            Marshal.Copy(codeBytes, 0, codePtr, codeBytes.Length);
            int result = _connect(codePtr, (uint)codeBytes.Length);
            CrashLog.Log($"[MP Bridge] DoConnect: mp_connect returned {result}");

            if (result == 1)
            {
                SetJoinState(JOIN_WAITING_FOR_SAVE);
                _logger.Msg($"[MP Bridge] Joining room {code}...");
                CrashLog.Log($"[MP Join] State → WaitingForSave");
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField($"Multiplayer: Joining room {code}..."); } catch { }
                HideMultiplayerPanel();
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
        ResetJoinState();
        if (_disconnect == null)
        {
            _logger.Warning("[MP Bridge] mp_disconnect export not available.");
            return;
        }

        _disconnect();
        _isHosting = false;
        _displayRoomCode = "";
        _discoveredSavePath = null;
        _cachedSaveData = null;
        _cachedSaveAge = 0f;

        // Clean up MP save files and restore originals
        CleanupMpSaveFiles();

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
        ResetJoinState();
        if (_disconnect == null)
        {
            _logger.Warning("[MP Bridge] mp_disconnect export not available.");
            return;
        }

        _disconnect();
        _isHosting = false;
        _displayRoomCode = "";
        _discoveredSavePath = null;
        _cachedSaveData = null;
        _cachedSaveAge = 0f;

        // Clean up MP save files (host might have _mp_temp leftovers)
        CleanupMpSaveFiles();
        _logger.Msg("[MP Bridge] Stopped hosting.");

        try
        {
            var ui = StaticUIElements.instance;
            if (ui != null)
                ui.AddMeesageInField("Multiplayer: Stopped hosting.");
        }
        catch { }
    }



    private void SendSaveToClients()
    {
        try
        {
            CrashLog.Log("[MP Save] Host: sending save to clients...");

            byte[] saveData;

            // Check if we have a recent cached save (avoids re-saving when multiple clients join)
            if (_cachedSaveData != null && _cachedSaveAge < SAVE_CACHE_LIFETIME)
            {
                saveData = _cachedSaveData;
                CrashLog.Log($"[MP Save] Using cached save data ({saveData.Length} bytes, {_cachedSaveAge:F1}s old)");
            }
            else
            {
                // Save to a temp name so we don't pollute the save directory
                string tempSaveName = "_mp_temp";

                try
                {
                    SaveSystem.SaveGame(tempSaveName, tempSaveName);
                    CrashLog.Log("[MP Save] SaveGame(\"_mp_temp\", \"_mp_temp\") OK");
                }
                catch (Exception ex)
                {
                    CrashLog.Log($"[MP Save] SaveGame with temp name failed: {ex.Message} — falling back to parameterless SaveGame");
                    try { SaveSystem.SaveGame(); }
                    catch (Exception ex2) { CrashLog.LogException("MP Save: SaveGame()", ex2); return; }
                }

                // Give the save a moment to flush to disk
                System.Threading.Thread.Sleep(300);

                // Try to find the temp save file first; fall back to newest save
                string saveDirPath = null;
                try { saveDirPath = SaveSystem.saveDirPath; }
                catch { }

                string savePath = null;
                bool isTempFile = false;

                if (!string.IsNullOrEmpty(saveDirPath))
                {
                    string tempPath = Path.Combine(saveDirPath, tempSaveName + ".save");
                    if (File.Exists(tempPath))
                    {
                        savePath = tempPath;
                        isTempFile = true;
                        CrashLog.Log($"[MP Save] Found temp save: {tempPath}");
                    }
                }

                if (savePath == null)
                    savePath = DiscoverSaveFile();

                if (savePath == null)
                {
                    CrashLog.Log("[MP Save] ERROR: Could not find any save file!");
                    _logger.Error("[MP Save] Could not locate save file to send.");
                    return;
                }

                saveData = File.ReadAllBytes(savePath);
                CrashLog.Log($"[MP Save] Read {saveData.Length} bytes from {savePath}");

                if (saveData.Length == 0)
                {
                    CrashLog.Log("[MP Save] ERROR: Save file is empty!");
                    if (isTempFile) TryDeleteFile(savePath);
                    return;
                }

                // Clean up temp file
                if (isTempFile) TryDeleteFile(savePath);
                try { SaveSystem.DeleteSaveFile(tempSaveName); } catch { }

                // Cache for future requests
                _cachedSaveData = saveData;
                _cachedSaveAge = 0f;
                CrashLog.Log($"[MP Save] Cached {saveData.Length} bytes for {SAVE_CACHE_LIFETIME}s");
            }

            // Pass to Rust for chunked transfer
            IntPtr ptr = Marshal.AllocHGlobal(saveData.Length);
            try
            {
                Marshal.Copy(saveData, 0, ptr, saveData.Length);
                int result = _sendSaveData(ptr, (uint)saveData.Length);
                CrashLog.Log($"[MP Save] mp_send_save_data returned {result}");

                if (result == 1)
                {
                    _logger.Msg("[MP Save] Save data queued for transfer.");
                    try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Sending save to client..."); } catch { }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("SendSaveToClients", ex);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            CrashLog.Log($"[MP Save] Deleted temp file: {path}");
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Save] Could not delete temp file: {ex.Message}");
        }
    }

    /// Cleans up multiplayer save artifacts: _mp_sync files, .mp_backup files.
    /// Restores original saves from backups so the player's own world is intact after disconnect.
    private void CleanupMpSaveFiles()
    {
        string saveDir = DiscoverSaveDirectory();
        if (saveDir == null)
        {
            CrashLog.Log("[MP Cleanup] No save directory found — skipping cleanup");
            return;
        }

        int cleaned = 0;

        try
        {
            // Delete _mp_sync.* and _mp_temp.* files
            foreach (var file in Directory.GetFiles(saveDir))
            {
                string name = Path.GetFileNameWithoutExtension(file).ToLower();
                if (name == "_mp_sync" || name == "_mp_temp")
                {
                    TryDeleteFile(file);
                    cleaned++;
                }
            }

            // Restore .mp_backup files → undo the overwrite from WriteSaveToDisk
            foreach (var backupFile in Directory.GetFiles(saveDir, "*.mp_backup"))
            {
                string originalPath = backupFile.Substring(0, backupFile.Length - ".mp_backup".Length);
                try
                {
                    File.Copy(backupFile, originalPath, true);
                    File.Delete(backupFile);
                    CrashLog.Log($"[MP Cleanup] Restored original save: {Path.GetFileName(originalPath)}");
                    cleaned++;
                }
                catch (Exception ex)
                {
                    CrashLog.Log($"[MP Cleanup] Failed to restore {Path.GetFileName(backupFile)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Cleanup] Error during cleanup: {ex.Message}");
        }

        if (cleaned > 0)
            CrashLog.Log($"[MP Cleanup] Cleaned up {cleaned} multiplayer save file(s)");
        else
            CrashLog.Log("[MP Cleanup] No multiplayer save files to clean up");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Client Join: State Machine Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private bool IsInMainMenu()
    {
        if (!string.IsNullOrEmpty(_currentSceneName))
            return _currentSceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase);

        // Fallback: query scene manager directly
        try
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
            _currentSceneName = scene;
            return scene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Called from OnUpdate when joinState == SaveReceived.
    /// Fetches bytes from Rust, writes them to disk, then decides how to load.
    /// </summary>
    private void FetchAndProcessSave()
    {
        try
        {
            // ── 1. Grab the raw bytes from Rust ──
            uint size = _getSaveDataSize != null ? _getSaveDataSize() : 0;
            if (size == 0)
            {
                CrashLog.Log("[MP Join] Pending save has 0 bytes — aborting");
                ResetJoinState();
                return;
            }

            CrashLog.Log($"[MP Join] Fetching {size} bytes from Rust...");
            byte[] saveData = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal((int)size);
            try
            {
                uint copied = _getSaveData(ptr, size);
                Marshal.Copy(ptr, saveData, 0, (int)copied);
                CrashLog.Log($"[MP Join] Got {copied} bytes");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // Peek first bytes for diagnostics
            if (saveData.Length > 0)
            {
                int peekLen = Math.Min(saveData.Length, 200);
                string peekText = System.Text.Encoding.UTF8.GetString(saveData, 0, peekLen).Replace("\r", "").Replace("\n", "\\n");
                CrashLog.Log($"[MP Join] First {peekLen} bytes: {peekText}");
            }

            _pendingSaveBytes = saveData;

            // Tell Rust we consumed the buffer
            if (_saveLoadComplete != null) _saveLoadComplete();

            // ── 2. Write to disk ──
            WriteSaveToDisk();

            // ── 3. Attempt load (scene-aware) ──
            if (_pendingSaveName == null)
            {
                CrashLog.Log("[MP Join] ERROR: WriteSaveToDisk failed to produce a save name");
                _logger.Error("[MP Join] Failed to write save to disk.");
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Failed to write host save!"); } catch { }
                ResetJoinState();
                return;
            }

            // Decide how to load based on current scene
            CrashLog.Log($"[MP Join] Current scene: \"{_currentSceneName}\"");

            if (IsInMainMenu())
            {
                // From MainMenu: SaveSystem.Load() does NOT trigger a scene transition
                // (onLoadingData callbacks aren't registered yet).
                // We must: set loadSaveName, then manually load the game scene.
                CrashLog.Log("[MP Join] In MainMenu — initiating manual scene transition");
                InitiateSceneTransition();
            }
            else
            {
                // Already in-game: SaveSystem.Load() should work (callbacks are registered)
                AttemptSaveLoad();
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("FetchAndProcessSave", ex);
            _logger.Error($"[MP Join] Exception during save processing: {ex.Message}");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Error processing host save!"); } catch { }
            ResetJoinState();
        }
    }

    /// <summary>
    /// Writes _pendingSaveBytes to disk. Handles both "overwrite existing" and "fresh install" cases.
    /// Sets _pendingSaveName and _pendingSaveFullPath on success.
    /// </summary>
    private void WriteSaveToDisk()
    {
        DumpSaveSystemMethods();

        string saveDir = DiscoverSaveDirectory();
        if (saveDir == null)
        {
            CrashLog.Log("[MP Join] ERROR: Could not find save directory!");
            // Last resort: use persistentDataPath directly
            saveDir = Application.persistentDataPath;
            try { Directory.CreateDirectory(saveDir); } catch { }
        }

        // ── Scan existing saves ──
        string existingSaveName = null;
        string existingSavePath = null;
        string ext = ".save";

        try
        {
            var existingFiles = Directory.GetFiles(saveDir);
            CrashLog.Log($"[MP Join] Save directory has {existingFiles.Length} files:");
            foreach (var f in existingFiles)
            {
                string fname = Path.GetFileName(f);
                string fext = Path.GetExtension(f).ToLower();
                var finfo = new FileInfo(f);
                CrashLog.Log($"[MP Join]   {fname} ({finfo.Length} bytes, {finfo.LastWriteTime:HH:mm:ss})");

                if (fext == ".save" || fext == ".json" || fext == ".sav" || fext == ".dat")
                {
                    ext = fext;
                    string nameNoExt = Path.GetFileNameWithoutExtension(f);
                    if (nameNoExt.StartsWith("_mp_")) continue;
                    if (fext == ".vdf") continue;

                    if (existingSavePath == null || finfo.LastWriteTime > new FileInfo(existingSavePath).LastWriteTime)
                    {
                        existingSaveName = nameNoExt;
                        existingSavePath = f;
                    }
                }
            }
        }
        catch (Exception ex) { CrashLog.Log($"[MP Join] Error scanning save dir: {ex.Message}"); }

        // ── Always write a debug/_mp_sync copy ──
        string tempPath = Path.Combine(saveDir, "_mp_sync" + ext);
        File.WriteAllBytes(tempPath, _pendingSaveBytes);
        CrashLog.Log($"[MP Join] Wrote debug copy: {tempPath}");

        // ── Strategy A: Overwrite an existing save (game already knows about it) ──
        if (existingSaveName != null && existingSavePath != null)
        {
            // Backup the original
            string backupPath = existingSavePath + ".mp_backup";
            try
            {
                File.Copy(existingSavePath, backupPath, true);
                CrashLog.Log($"[MP Join] Backed up: {existingSavePath} -> {backupPath}");
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[MP Join] Backup warning: {ex.Message}");
            }

            File.WriteAllBytes(existingSavePath, _pendingSaveBytes);
            _pendingSaveName = existingSaveName;
            _pendingSaveFullPath = existingSavePath;
            CrashLog.Log($"[MP Join] Overwrote existing save: \"{existingSaveName}\" at {existingSavePath} ({_pendingSaveBytes.Length} bytes)");
            return;
        }

        // ── Strategy B: No existing save (fresh install) — create one with a timestamp name ──
        CrashLog.Log("[MP Join] No existing save found — creating new save file for fresh install");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string newPath = Path.Combine(saveDir, timestamp + ext);
        File.WriteAllBytes(newPath, _pendingSaveBytes);
        _pendingSaveName = timestamp;
        _pendingSaveFullPath = newPath;
        CrashLog.Log($"[MP Join] Created new save: \"{timestamp}\" at {newPath} ({_pendingSaveBytes.Length} bytes)");
    }

    /// <summary>
    /// Called when client is in MainMenu: sets loadSaveName on SaveSystem and
    /// triggers a scene transition to the game scene. After the scene loads,
    /// OnSceneLoaded → deferred delay → AttemptSaveLoad() will apply the save.
    /// </summary>
    private void InitiateSceneTransition()
    {
        // Store room code for auto-reconnect after scene transition
        _reconnectRoomCode = _roomCode?.Trim().ToUpper();
        if (string.IsNullOrEmpty(_reconnectRoomCode))
        {
            // Try to get it from Rust state
            try
            {
                IntPtr codePtr = _getRoomCode != null ? _getRoomCode() : IntPtr.Zero;
                if (codePtr != IntPtr.Zero)
                {
                    string code = Marshal.PtrToStringAnsi(codePtr);
                    if (!string.IsNullOrEmpty(code)) _reconnectRoomCode = code;
                }
            }
            catch { }
        }
        CrashLog.Log($"[MP Join] Stored room code for reconnect: \"{_reconnectRoomCode}\"");

        // ── Approach 1: Use the game's own MainMenu.Continue() ──
        // This replicates what happens when the player presses "Continue" on the
        // main menu. The game handles isQuitting, scene transitions, save loading,
        // callbacks, and all internal state setup. Much safer than manual scene load.
        try
        {
            var menus = Resources.FindObjectsOfTypeAll<Il2Cpp.MainMenu>();
            if (menus != null && menus.Count > 0)
            {
                var mainMenu = menus[0];
                CrashLog.Log("[MP Join] Found MainMenu instance — using game's Continue() flow");

                // The save we overwrote in WriteSaveToDisk is the newest save,
                // so Continue() will load it through the normal game path.
                _gameHandledSaveLoad = true;
                SetJoinState(JOIN_LOADING_SCENE);
                _pendingSaveBytes = null; // free memory

                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Loading host's game..."); } catch { }

                mainMenu.Continue();
                CrashLog.Log("[MP Join] MainMenu.Continue() called — game will handle scene transition and save load");
                return;
            }
            else
            {
                CrashLog.Log("[MP Join] No MainMenu instance found — falling back to manual approach");
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Join] MainMenu.Continue() failed: {ex.GetType().Name}: {ex.Message} — falling back to manual approach");
            _gameHandledSaveLoad = false;
        }

        // ── Approach 2: Manual scene transition (fallback) ──
        CrashLog.Log("[MP Join] Using manual scene transition fallback");

        // Reset isQuitting flag — the game sets this when quitting to MainMenu
        // and never resets it, which causes crashes during save load
        try
        {
            bool wasQuitting = SaveSystem.isQuitting;
            if (wasQuitting)
            {
                SaveSystem.isQuitting = false;
                CrashLog.Log($"[MP Join] Reset SaveSystem.isQuitting (was {wasQuitting})");
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Join] Could not reset isQuitting: {ex.Message}");
        }

        // Set SaveSystem.loadSaveName so the game knows which save to load
        try
        {
            SaveSystem.loadSaveName = _pendingSaveName;
            CrashLog.Log($"[MP Join] Set SaveSystem.loadSaveName = \"{_pendingSaveName}\"");
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Join] Failed to set loadSaveName: {ex.Message}");
        }

        // Enumerate available scenes and find the game scene
        try
        {
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            CrashLog.Log($"[MP Join] Build has {sceneCount} scenes:");
            string gameSceneName = null;
            int gameSceneIndex = -1;

            for (int i = 0; i < sceneCount; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                CrashLog.Log($"[MP Join]   [{i}] \"{name}\" ({path})");

                if (!name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("Init", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("Splash", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("Loading", StringComparison.OrdinalIgnoreCase))
                {
                    if (gameSceneName == null)
                    {
                        gameSceneName = name;
                        gameSceneIndex = i;
                    }
                }
            }

            if (gameSceneIndex >= 0)
            {
                CrashLog.Log($"[MP Join] Loading game scene: [{gameSceneIndex}] \"{gameSceneName}\"");
                SetJoinState(JOIN_LOADING_SCENE);
                _pendingSaveBytes = null;
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Loading host's game..."); } catch { }
                SceneManager.LoadScene(gameSceneIndex);
                return;
            }
            else
            {
                CrashLog.Log("[MP Join] Could not identify game scene — trying build index 1");
                SetJoinState(JOIN_LOADING_SCENE);
                _pendingSaveBytes = null;
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Loading host's game..."); } catch { }
                SceneManager.LoadScene(1);
                return;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Join] Scene enumeration failed: {ex.GetType().Name}: {ex.Message}");
            CrashLog.Log("[MP Join] Falling back to SceneManager.LoadScene(1)");
            SetJoinState(JOIN_LOADING_SCENE);
            _pendingSaveBytes = null;
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Loading host's game..."); } catch { }
            try { SceneManager.LoadScene(1); }
            catch (Exception ex2) { CrashLog.Log($"[MP Join] LoadScene(1) failed: {ex2.Message}"); }
        }
    }

    /// <summary>
    /// Called after the game scene has loaded (from WaitingForGameScene state).
    /// Applies the save via SaveSystem.Load() — now the onLoadingData callbacks ARE registered.
    /// Then triggers auto-reconnect to the relay.
    /// </summary>
    private void AttemptSaveLoad()
    {
        if (_pendingSaveName == null)
        {
            CrashLog.Log("[MP Join] AttemptSaveLoad: no pending save name — aborting");
            ResetJoinState();
            return;
        }

        bool loaded = false;
        CrashLog.Log($"[MP Join] AttemptSaveLoad: name=\"{_pendingSaveName}\", scene=\"{_currentSceneName}\"");

        // Reset isQuitting flag — the game sets this when quitting to MainMenu
        // and never resets it, which causes crashes during save load
        try
        {
            bool wasQuitting = SaveSystem.isQuitting;
            SaveSystem.isQuitting = false;
            CrashLog.Log($"[MP Join] SaveSystem.isQuitting = false (was {wasQuitting})");
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Join] Could not reset isQuitting: {ex.Message}");
        }

        // ── Approach A: Load(name, false) — standard load path ──
        CrashLog.Log($"[MP Join] Approach A: SaveSystem.Load(\"{_pendingSaveName}\", false)...");
        try
        {
            SaveSystem.Load(_pendingSaveName, false);
            CrashLog.Log("[MP Join] Approach A returned OK");
            loaded = true;
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Join] Approach A threw: {ex.GetType().Name}: {ex.Message}");
        }

        // ── Approach B: Load(name, true) — "from pause menu" path ──
        if (!loaded)
        {
            CrashLog.Log($"[MP Join] Approach B: SaveSystem.Load(\"{_pendingSaveName}\", true)...");
            try
            {
                SaveSystem.Load(_pendingSaveName, true);
                CrashLog.Log("[MP Join] Approach B returned OK");
                loaded = true;
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[MP Join] Approach B threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── Approach C: Try _mp_sync name directly ──
        if (!loaded)
        {
            CrashLog.Log("[MP Join] Approach C: SaveSystem.Load(\"_mp_sync\", false)...");
            try
            {
                SaveSystem.Load("_mp_sync", false);
                CrashLog.Log("[MP Join] Approach C returned OK");
                loaded = true;
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[MP Join] Approach C threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── Approach D: Reflection — LoadGame(string) + LoadGameData() ──
        if (!loaded)
        {
            CrashLog.Log("[MP Join] Approach D: Trying reflection-based load...");
            try
            {
                var ssType = typeof(SaveSystem);
                var loadGame = ssType.GetMethod("LoadGame",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                    null, new Type[] { typeof(string) }, null);
                if (loadGame != null)
                {
                    CrashLog.Log($"[MP Join] Found LoadGame(string), invoking with \"{_pendingSaveName}\"...");
                    loadGame.Invoke(null, new object[] { _pendingSaveName });
                    CrashLog.Log("[MP Join] Approach D (LoadGame) returned OK — now calling LoadGameData()...");
                    try { SaveSystem.LoadGameData(); CrashLog.Log("[MP Join] LoadGameData() OK"); }
                    catch (Exception ex3) { CrashLog.Log($"[MP Join] LoadGameData() threw: {ex3.Message}"); }
                    loaded = true;
                }
                else
                {
                    CrashLog.Log("[MP Join] LoadGame(string) not found via reflection");
                }
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[MP Join] Approach D threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        CrashLog.Log($"[MP Join] AttemptSaveLoad finished: loaded={loaded}");

        if (loaded)
        {
            SetJoinState(JOIN_LOADED);
            _pendingSaveBytes = null; // free memory
            _logger.Msg("[MP Join] Save loaded from host!");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Save loaded from host!"); } catch { }

            // Only reconnect if the relay actually died during scene transition
            if (_reconnectRoomCode != null)
            {
                bool relayStillAlive = _isRelayActive != null && _isRelayActive() != 0;
                bool stillConnected = _isConnected != null && _isConnected() != 0;

                if (relayStillAlive && stillConnected)
                {
                    CrashLog.Log($"[MP Join] Relay still alive after save load (alive={relayStillAlive}, connected={stillConnected}) — no reconnect needed");
                }
                else
                {
                    CrashLog.Log($"[MP Join] Relay died during save load (alive={relayStillAlive}, connected={stillConnected}) — auto-reconnecting to {_reconnectRoomCode}");
                    AutoReconnect();
                }
            }
        }
        else
        {
            CrashLog.Log("[MP Join] All load approaches failed — giving up");
            _logger.Warning("[MP Join] Could not load save — check dc_modloader_debug.log for details.");
            try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Failed to load host save! Check logs."); } catch { }
            ResetJoinState();
        }
    }

    /// <summary>
    /// Auto-reconnects to the relay after a scene transition, skipping save re-request.
    /// </summary>
    private void AutoReconnect()
    {
        if (_connect == null || string.IsNullOrEmpty(_reconnectRoomCode))
        {
            CrashLog.Log("[MP Join] AutoReconnect: no connect delegate or room code");
            return;
        }

        CrashLog.Log($"[MP Join] AutoReconnect: joining room {_reconnectRoomCode} (skip save = true)");
        _skipSaveOnReconnect = true;
        _reconnectCooldown = 5f;

        // Tell Rust not to request save on reconnect
        if (_skipNextSaveRequest != null)
        {
            _skipNextSaveRequest();
        }

        byte[] codeBytes = System.Text.Encoding.UTF8.GetBytes(_reconnectRoomCode);
        IntPtr codePtr = Marshal.AllocHGlobal(codeBytes.Length);
        try
        {
            Marshal.Copy(codeBytes, 0, codePtr, codeBytes.Length);
            int result = _connect(codePtr, (uint)codeBytes.Length);
            CrashLog.Log($"[MP Join] AutoReconnect: mp_connect returned {result}");

            if (result == 1)
            {
                SetJoinState(JOIN_WAITING_FOR_SAVE);
                try { var ui = StaticUIElements.instance; if (ui != null) ui.AddMeesageInField("Multiplayer: Reconnecting..."); } catch { }
            }
            else
            {
                CrashLog.Log("[MP Join] AutoReconnect failed");
                _skipSaveOnReconnect = false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(codePtr);
        }
    }

    /// <summary>
    /// Resets join state back to Idle and clears pending data.
    /// </summary>
    private void ResetJoinState()
    {
        SetJoinState(JOIN_IDLE);
        _pendingSaveBytes = null;
        _pendingSaveName = null;
        _pendingSaveFullPath = null;
        _deferredLoadDelay = 0f;
        _skipSaveOnReconnect = false;
        _reconnectCooldown = 0f;
        _gameHandledSaveLoad = false;
        // Note: _reconnectRoomCode is intentionally NOT cleared here
        // so auto-reconnect can still work after scene transitions.
    }

    private void DumpSaveSystemMethods()
    {
        try
        {
            CrashLog.Log("[MP Save] === SaveSystem method dump ===");
            var ssType = typeof(SaveSystem);
            var methods = ssType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
            foreach (var m in methods)
            {
                var parms = m.GetParameters();
                var parmStr = string.Join(", ", Array.ConvertAll(parms, p => $"{p.ParameterType.Name} {p.Name}"));
                CrashLog.Log($"[MP Save]   {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({parmStr})");
            }
            CrashLog.Log("[MP Save] === end SaveSystem dump ===");

            // Also dump static fields/properties
            var fields = ssType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (fields.Length > 0)
            {
                CrashLog.Log("[MP Save] === SaveSystem fields ===");
                foreach (var f in fields)
                {
                    try
                    {
                        var val = f.GetValue(null);
                        CrashLog.Log($"[MP Save]   {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name} = {val}");
                    }
                    catch
                    {
                        CrashLog.Log($"[MP Save]   {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name} = <error reading>");
                    }
                }
                CrashLog.Log("[MP Save] === end SaveSystem fields ===");
            }

            var props = ssType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (props.Length > 0)
            {
                CrashLog.Log("[MP Save] === SaveSystem properties ===");
                foreach (var p in props)
                {
                    try
                    {
                        var val = p.GetValue(null);
                        CrashLog.Log($"[MP Save]   {p.PropertyType.Name} {p.Name} = {val}");
                    }
                    catch
                    {
                        CrashLog.Log($"[MP Save]   {p.PropertyType.Name} {p.Name} = <error reading>");
                    }
                }
                CrashLog.Log("[MP Save] === end SaveSystem properties ===");
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Save] SaveSystem reflection failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string DiscoverSaveDirectory()
    {
        if (_discoveredSavePath != null)
        {
            string dir = Path.GetDirectoryName(_discoveredSavePath);
            if (Directory.Exists(dir)) return dir;
        }

        string basePath = Application.persistentDataPath;
        CrashLog.Log($"[MP Save] persistentDataPath = {basePath}");

        // Check common save subdirectories
        string[] subDirs = { "Saves", "SaveGames", "Save", "" };
        foreach (var sub in subDirs)
        {
            string candidate = string.IsNullOrEmpty(sub) ? basePath : Path.Combine(basePath, sub);
            if (Directory.Exists(candidate))
            {
                // Check if it has any save-looking files
                try
                {
                    var files = Directory.GetFiles(candidate);
                    if (files.Length > 0)
                    {
                        CrashLog.Log($"[MP Save] Found save directory: {candidate} ({files.Length} files)");
                        return candidate;
                    }
                }
                catch { }
            }
        }

        // Fallback: use persistentDataPath directly
        CrashLog.Log($"[MP Save] Using persistentDataPath as save directory: {basePath}");
        return basePath;
    }

    private string DiscoverSaveFile()
    {
        if (_discoveredSavePath != null && File.Exists(_discoveredSavePath))
            return _discoveredSavePath;

        string basePath = Application.persistentDataPath;
        CrashLog.Log($"[MP Save] Searching for save files in: {basePath}");

        // Log directory contents for debugging
        try
        {
            LogDirectoryContents(basePath, 0);
        }
        catch (Exception ex) { CrashLog.Log($"[MP Save] Error listing dir: {ex.Message}"); }

        // Strategy: find the most recently modified save file
        string bestFile = null;
        DateTime bestTime = DateTime.MinValue;

        string[] searchDirs = { basePath };
        try
        {
            // Also search subdirectories
            var subDirs = Directory.GetDirectories(basePath);
            var allDirs = new List<string>(subDirs);
            allDirs.Insert(0, basePath);
            searchDirs = allDirs.ToArray();
        }
        catch { }

        string[] saveExtensions = { ".json", ".sav", ".save", ".dat" };

        foreach (var dir in searchDirs)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (Array.IndexOf(saveExtensions, ext) < 0) continue;

                    var info = new FileInfo(file);
                    CrashLog.Log($"[MP Save] Candidate: {file} (size={info.Length}, modified={info.LastWriteTime:HH:mm:ss})");

                    if (info.LastWriteTime > bestTime)
                    {
                        bestTime = info.LastWriteTime;
                        bestFile = file;
                    }
                }
            }
            catch { }
        }

        if (bestFile != null)
        {
            CrashLog.Log($"[MP Save] Selected save file: {bestFile}");
            _discoveredSavePath = bestFile;
        }

        return bestFile;
    }

    private void LogDirectoryContents(string path, int depth)
    {
        if (depth > 2) return; // don't recurse too deep
        string indent = new string(' ', depth * 2);

        try
        {
            foreach (var file in Directory.GetFiles(path))
            {
                var info = new FileInfo(file);
                CrashLog.Log($"[MP Save] {indent}FILE: {Path.GetFileName(file)} ({info.Length} bytes, {info.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
            }
            foreach (var dir in Directory.GetDirectories(path))
            {
                CrashLog.Log($"[MP Save] {indent}DIR: {Path.GetFileName(dir)}/");
                LogDirectoryContents(dir, depth + 1);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log($"[MP Save] {indent}Error: {ex.Message}");
        }
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
        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                _mpDisabledEventSystem = es;
                es.enabled = false;
            }
        }
        catch { }
    }

    public void HideMultiplayerPanel()
    {
        _showPanel = false;
        if (_mpDisabledEventSystem != null)
            _mpReenableCountdown = 2;
    }

    /// <summary>
    /// Called from Core.OnGUI(). Draws the multiplayer panel if toggled on.
    /// </summary>
    public void DrawGUI()
    {
        // Show join progress overlay even when panel is hidden
        uint drawJoinState = GetJoinState();
        if (drawJoinState != JOIN_IDLE && drawJoinState != JOIN_LOADED)
        {
            DrawJoinOverlay();
        }

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

        uint guiJoinState = GetJoinState();
        bool joinBlocked = !_initialized || (guiJoinState != JOIN_IDLE && guiJoinState != JOIN_LOADED);
        if (joinBlocked) GUI.enabled = false;
        string joinLabel;
        if (!_initialized)
            joinLabel = "JOIN GAME  (waiting...)";
        else if (guiJoinState == JOIN_WAITING_FOR_SAVE)
            joinLabel = "JOINING...  (receiving save)";
        else if (guiJoinState == JOIN_SAVE_READY || guiJoinState == JOIN_LOADING_SCENE)
            joinLabel = "JOINING...  (loading game)";
        else
            joinLabel = "JOIN GAME";
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

    private void DrawJoinOverlay()
    {
        if (!_stylesInitialized) InitStyles();

        // Semi-transparent dark background strip
        float stripH = 60f;
        float stripY = Screen.height * 0.4f;

        if (_overlayBg == null)
            _overlayBg = MakeTex(1, 1, new Color(0f, 0f, 0f, 0.75f));

        GUI.DrawTexture(new Rect(0, stripY, Screen.width, stripH), _overlayBg);

        // Build progress text
        string text;
        float progress = _getSaveTransferProgress != null ? _getSaveTransferProgress() : -1f;
        uint totalBytes = _getSaveTransferTotalBytes != null ? _getSaveTransferTotalBytes() : 0;

        uint overlayJoinState = GetJoinState();
        if (overlayJoinState == JOIN_WAITING_FOR_SAVE)
        {
            if (progress >= 0f && totalBytes >= 1_000_000)
            {
                int pct = (int)(progress * 100f);
                text = $"Loading Game {pct}%";
            }
            else if (progress >= 0f)
            {
                text = "Loading Game...";
            }
            else
            {
                text = "Connecting...";
            }
        }
        else if (overlayJoinState == JOIN_SAVE_READY)
        {
            text = "Processing save...";
        }
        else
        {
            text = "Loading Game...";
        }

        GUI.Label(new Rect(0, stripY, Screen.width, stripH), text, _overlayTextStyle);
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

        _overlayTextStyle = new GUIStyle();
        _overlayTextStyle.font = defaultFont;
        _overlayTextStyle.fontSize = 24;
        _overlayTextStyle.fontStyle = FontStyle.Bold;
        _overlayTextStyle.alignment = TextAnchor.MiddleCenter;
        _overlayTextStyle.normal.textColor = Color.white;

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



    private static void CleanupAll()
    {

        EntityManager.DestroyAll();
    }

    public void Shutdown()
    {
        if (_initialized && _disconnect != null)
        {
            try { _disconnect(); }
            catch { }
        }

        CleanupMpSaveFiles();
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

    public Transform followTarget;
    public float offsetY = 2.05f;
    private float _smoothY = float.NaN;

    void LateUpdate()
    {

        // Follow the target (remote player GO) — nametag is NOT parented to avoid scale inheritance
        // XZ follows instantly; Y is heavily smoothed to prevent micro-jitter that
        // causes motion-blur fuzz on the text.
        if (followTarget != null)
        {
            float desiredY = followTarget.position.y + offsetY;
            if (float.IsNaN(_smoothY))
                _smoothY = desiredY;                   // first frame: snap
            else
                _smoothY = Mathf.Lerp(_smoothY, desiredY, Time.deltaTime * 3f); // slow follow

            transform.position = new Vector3(
                followTarget.position.x,
                _smoothY,
                followTarget.position.z);
        }
        else
        {
            CrashLog.Log($"[MP Billboard] followTarget is NULL — destroying nametag '{gameObject.name}'");
            UnityEngine.Object.Destroy(gameObject);
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;
        transform.LookAt(transform.position + cam.transform.forward);
    }
}
