using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2Cpp;
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

    // v4
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

    // v5
    public IntPtr RegisterCustomEmployee;
    public IntPtr IsCustomEmployeeHired;
    public IntPtr FireCustomEmployee;
    public IntPtr RegisterSalary;

    // v6
    public IntPtr ShowNotification;
    public IntPtr GetMoneyPerSecond;
    public IntPtr GetExpensesPerSecond;
    public IntPtr GetXpPerSecond;
    public IntPtr IsGamePaused;
    public IntPtr SetGamePaused;
    public IntPtr GetDifficulty;
    public IntPtr TriggerSave;

    // v7 - Steam / Multiplayer
    public IntPtr SteamGetMyId;
    public IntPtr SteamGetFriendName;
    public IntPtr SteamCreateLobby;
    public IntPtr SteamJoinLobby;
    public IntPtr SteamLeaveLobby;
    public IntPtr SteamGetLobbyId;
    public IntPtr SteamGetLobbyOwner;
    public IntPtr SteamGetLobbyMemberCount;
    public IntPtr SteamGetLobbyMemberByIndex;
    public IntPtr SteamSetLobbyData;
    public IntPtr SteamGetLobbyData;
    public IntPtr SteamSendP2P;
    public IntPtr SteamIsP2PAvailable;
    public IntPtr SteamReadP2P;
    public IntPtr SteamAcceptP2P;
    public IntPtr SteamPollEvent;
    public IntPtr GetPlayerPosition;

    // v8 - Mod Configuration
    public IntPtr ConfigRegisterBool;
    public IntPtr ConfigRegisterInt;
    public IntPtr ConfigRegisterFloat;
    public IntPtr ConfigGetBool;
    public IntPtr ConfigGetInt;
    public IntPtr ConfigGetFloat;

    public IntPtr SpawnCharacter;
    public IntPtr DestroyEntity;
    public IntPtr SetEntityPosition;
    public IntPtr IsEntityReady;
    public IntPtr SetEntityAnimation;
    public IntPtr GetPrefabCount;
    public IntPtr SetEntityName;

    public IntPtr GetPlayerCarryState;
    public IntPtr GetPlayerCrouching;
    public IntPtr GetPlayerSitting;
    public IntPtr SetEntityCrouching;
    public IntPtr SetEntitySitting;

    public IntPtr SetEntityCarryAnim;
    public IntPtr CreateEntityCarryVisual;
    public IntPtr DestroyEntityCarryVisual;

    public IntPtr GetDefaultSpawnPosition;
    public IntPtr WarpLocalPlayer;

    public IntPtr GetEntityPosition;
    public IntPtr AddEntityCollider;
    public IntPtr SetEntityCarryTransform;

    // v13 - World Object Sync
    public IntPtr WorldGetObjectCount;
    public IntPtr WorldGetObjectHashes;
    public IntPtr WorldGetObjectState;
    public IntPtr WorldSpawnObject;
    public IntPtr WorldDestroyObject;
    public IntPtr WorldPlaceInRack;
    public IntPtr WorldRemoveFromRack;
    public IntPtr WorldSetPower;
    public IntPtr WorldSetProperty;
    public IntPtr WorldConnectCable;
    public IntPtr WorldDisconnectCable;
    public IntPtr WorldPickupObject;
    public IntPtr WorldDropObject;

    public IntPtr WorldEnsureRackUIDs;
}

// delegates stored as fields to prevent GC collection while rust holds pointers
public partial class GameAPIManager : IDisposable
{
    public const uint API_VERSION = 14;

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

    // v7 delegate types
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetULongDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetStringFromU64Delegate(ulong steamId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateLobbyDelegate(uint lobbyType, uint maxPlayers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int JoinLobbyDelegate(ulong lobbyId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetLobbyMemberDelegate(uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetLobbyDataDelegate(IntPtr key, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetLobbyDataDelegate(IntPtr key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SendP2PDelegate(ulong target, IntPtr data, uint len, uint reliable);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint IsP2PAvailableDelegate(IntPtr outSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadP2PDelegate(IntPtr buf, uint bufLen, IntPtr outSender);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AcceptP2PDelegate(ulong remote);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint PollEventDelegate(IntPtr outType, IntPtr outData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GetPlayerPositionDelegate(IntPtr outX, IntPtr outY, IntPtr outZ, IntPtr outRy);

    // v8 delegate types
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigRegisterBoolDelegate(IntPtr modId, IntPtr key, IntPtr displayName, uint defaultValue, IntPtr description);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigRegisterIntDelegate(IntPtr modId, IntPtr key, IntPtr displayName, int defaultValue, int min, int max, IntPtr description);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigRegisterFloatDelegate(IntPtr modId, IntPtr key, IntPtr displayName, float defaultValue, float min, float max, IntPtr description);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigGetBoolDelegate(IntPtr modId, IntPtr key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigGetIntDelegate(IntPtr modId, IntPtr key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float ConfigGetFloatDelegate(IntPtr modId, IntPtr key);

    // v9 delegate types
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SpawnCharacterDelegate(uint prefabIdx, float x, float y, float z, float rotY, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyEntityDelegate(uint entityId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetEntityPositionDelegate(uint entityId, float x, float y, float z, float rotY);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint IsEntityReadyDelegate(uint entityId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetEntityAnimationDelegate(uint entityId, float speed, uint isWalking);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetPrefabCountDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetEntityNameDelegate(uint entityId, IntPtr name);

    // v10 delegate types
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GetPlayerCarryStateDelegate(IntPtr outObjectInHand, IntPtr outNumObjects);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetPlayerCrouchingDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetPlayerSittingDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetEntityCrouchingDelegate(uint entityId, uint isCrouching);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetEntitySittingDelegate(uint entityId, uint isSitting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetEntityCarryAnimDelegate(uint entityId, uint isCarrying);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CreateEntityCarryVisualDelegate(uint entityId, uint objectInHandType);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyEntityCarryVisualDelegate(uint entityId);

    // v12 delegate types
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GetDefaultSpawnPositionDelegate(IntPtr outX, IntPtr outY, IntPtr outZ);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WarpLocalPlayerDelegate(float x, float y, float z);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetEntityPositionDelegate(uint entityId, IntPtr outX, IntPtr outY, IntPtr outZ);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddEntityColliderDelegate(uint entityId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetEntityCarryTransformDelegate(uint entityId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ);

    // v13 - World sync delegate types
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint WorldGetObjectCountDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint WorldGetObjectHashesDelegate(IntPtr buf, uint maxCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint WorldGetObjectStateDelegate(IntPtr id, uint idLen, IntPtr buf, uint bufMax);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldSpawnObjectDelegate(byte objectType, int prefabId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, IntPtr outId, uint outMax);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldDestroyObjectDelegate(IntPtr id, uint idLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldPlaceInRackDelegate(IntPtr id, uint idLen, int rackUid);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldRemoveFromRackDelegate(IntPtr id, uint idLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldSetPowerDelegate(IntPtr id, uint idLen, byte isOn);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldSetPropertyDelegate(IntPtr id, uint idLen, IntPtr key, uint keyLen, IntPtr val, uint valLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldConnectCableDelegate(int cableId, byte startType, float sx, float sy, float sz, IntPtr startDevice, uint startDeviceLen, byte endType, float ex, float ey, float ez, IntPtr endDevice, uint endDeviceLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldDisconnectCableDelegate(int cableId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldPickupObjectDelegate(IntPtr id, uint idLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WorldDropObjectDelegate(IntPtr id, uint idLen, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamNetworking_v006();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamUser_v023();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamFriends_v018();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong steamId);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SteamAPI_ISteamNetworking_SendP2PPacket(IntPtr self, ulong steamIDRemote, IntPtr pubData, uint cubData, int eP2PSendType, int nChannel);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SteamAPI_ISteamNetworking_IsP2PPacketAvailable(IntPtr self, out uint pcubMsgSize, int nChannel);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SteamAPI_ISteamNetworking_ReadP2PPacket(IntPtr self, IntPtr pubDest, uint cubDest, out uint pcubMsgSize, out ulong psteamIDRemote, int nChannel);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(IntPtr instancePtr, ulong steamIDRemote);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RegisterCustomEmployeeDelegate(IntPtr employeeId, IntPtr name, IntPtr description, float salary, float requiredReputation, uint confirmDialogs);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint IsCustomEmployeeHiredDelegate(IntPtr employeeId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FireCustomEmployeeDelegate(IntPtr employeeId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RegisterSalaryDelegate(int monthlySalary);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ShowNotificationDelegate(IntPtr message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetGamePausedDelegate(uint paused);

    // prevent GC while rust holds pointers
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
    // v5
    private readonly RegisterCustomEmployeeDelegate _registerCustomEmployee;
    private readonly IsCustomEmployeeHiredDelegate _isCustomEmployeeHired;
    private readonly FireCustomEmployeeDelegate _fireCustomEmployee;
    private readonly RegisterSalaryDelegate _registerSalary;
    // v6
    private readonly ShowNotificationDelegate _showNotification;
    private readonly GetFloatDelegate _getMoneyPerSecond, _getExpensesPerSecond, _getXpPerSecond;
    private readonly GetUIntDelegate _isGamePaused2;
    private readonly SetGamePausedDelegate _setGamePaused;
    private readonly GetIntDelegate _getDifficulty, _triggerSave;
    // v7
    private readonly GetULongDelegate _steamGetMyId;
    private readonly GetStringFromU64Delegate _steamGetFriendName;
    private readonly CreateLobbyDelegate _steamCreateLobby;
    private readonly JoinLobbyDelegate _steamJoinLobby;
    private readonly VoidDelegate _steamLeaveLobby;
    private readonly GetULongDelegate _steamGetLobbyId;
    private readonly GetULongDelegate _steamGetLobbyOwner;
    private readonly GetUIntDelegate _steamGetLobbyMemberCount;
    private readonly GetLobbyMemberDelegate _steamGetLobbyMemberByIndex;
    private readonly SetLobbyDataDelegate _steamSetLobbyData;
    private readonly GetLobbyDataDelegate _steamGetLobbyData;
    private readonly SendP2PDelegate _steamSendP2P;
    private readonly IsP2PAvailableDelegate _steamIsP2PAvailable;
    private readonly ReadP2PDelegate _steamReadP2P;
    private readonly AcceptP2PDelegate _steamAcceptP2P;
    private readonly PollEventDelegate _steamPollEvent;
    private readonly GetPlayerPositionDelegate _getPlayerPosition;
    // v8
    private readonly ConfigRegisterBoolDelegate _configRegisterBool;
    private readonly ConfigRegisterIntDelegate _configRegisterInt;
    private readonly ConfigRegisterFloatDelegate _configRegisterFloat;
    private readonly ConfigGetBoolDelegate _configGetBool;
    private readonly ConfigGetIntDelegate _configGetInt;
    private readonly ConfigGetFloatDelegate _configGetFloat;
    private readonly SpawnCharacterDelegate _spawnCharacter;
    private readonly DestroyEntityDelegate _destroyEntity;
    private readonly SetEntityPositionDelegate _setEntityPosition;
    private readonly IsEntityReadyDelegate _isEntityReady;
    private readonly SetEntityAnimationDelegate _setEntityAnimation;
    private readonly GetPrefabCountDelegate _getPrefabCount;
    private readonly SetEntityNameDelegate _setEntityName;
    private readonly GetPlayerCarryStateDelegate _getPlayerCarryState;
    private readonly GetPlayerCrouchingDelegate _getPlayerCrouching;
    private readonly GetPlayerSittingDelegate _getPlayerSitting;
    private readonly SetEntityCrouchingDelegate _setEntityCrouching;
    private readonly SetEntitySittingDelegate _setEntitySitting;
    private readonly SetEntityCarryAnimDelegate _setEntityCarryAnim;
    private readonly CreateEntityCarryVisualDelegate _createEntityCarryVisual;
    private readonly DestroyEntityCarryVisualDelegate _destroyEntityCarryVisual;

    private readonly GetDefaultSpawnPositionDelegate _getDefaultSpawnPosition;
    private readonly WarpLocalPlayerDelegate _warpLocalPlayer;

    GetEntityPositionDelegate _getEntityPosition;
    AddEntityColliderDelegate _addEntityCollider;
    SetEntityCarryTransformDelegate _setEntityCarryTransform;
    // v13
    WorldGetObjectCountDelegate _worldGetObjectCount;
    WorldGetObjectHashesDelegate _worldGetObjectHashes;
    WorldGetObjectStateDelegate _worldGetObjectState;
    WorldSpawnObjectDelegate _worldSpawnObject;
    WorldDestroyObjectDelegate _worldDestroyObject;
    WorldPlaceInRackDelegate _worldPlaceInRack;
    WorldRemoveFromRackDelegate _worldRemoveFromRack;
    WorldSetPowerDelegate _worldSetPower;
    WorldSetPropertyDelegate _worldSetProperty;
    WorldConnectCableDelegate _worldConnectCable;
    WorldDisconnectCableDelegate _worldDisconnectCable;
    WorldPickupObjectDelegate _worldPickupObject;
    WorldDropObjectDelegate _worldDropObject;
    GetIntDelegate _worldEnsureRackUIDs;

    private readonly MelonLogger.Instance _logger;
    private IntPtr _currentScenePtr = IntPtr.Zero;
    private IntPtr _friendNamePtr = IntPtr.Zero;
    private IntPtr _lobbyDataPtr = IntPtr.Zero;

    private IntPtr _steamNetworking = IntPtr.Zero;
    private IntPtr _steamUser = IntPtr.Zero;
    private IntPtr _steamFriends = IntPtr.Zero;

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

        _registerCustomEmployee = RegisterCustomEmployeeImpl;
        _isCustomEmployeeHired = IsCustomEmployeeHiredImpl;
        _fireCustomEmployee = FireCustomEmployeeImpl;
        _registerSalary = RegisterSalaryImpl;

        _showNotification = ShowNotificationImpl;
        _getMoneyPerSecond = GetMoneyPerSecondImpl;
        _getExpensesPerSecond = GetExpensesPerSecondImpl;
        _getXpPerSecond = GetXpPerSecondImpl;
        _isGamePaused2 = IsGamePausedImpl;
        _setGamePaused = SetGamePausedImpl;
        _getDifficulty = GetDifficultyImpl;
        _triggerSave = TriggerSaveImpl;

        _steamGetMyId = SteamGetMyIdImpl;
        _steamGetFriendName = SteamGetFriendNameImpl;
        _steamCreateLobby = SteamCreateLobbyImpl;
        _steamJoinLobby = SteamJoinLobbyImpl;
        _steamLeaveLobby = SteamLeaveLobbyImpl;
        _steamGetLobbyId = SteamGetLobbyIdImpl;
        _steamGetLobbyOwner = SteamGetLobbyOwnerImpl;
        _steamGetLobbyMemberCount = SteamGetLobbyMemberCountImpl;
        _steamGetLobbyMemberByIndex = SteamGetLobbyMemberByIndexImpl;
        _steamSetLobbyData = SteamSetLobbyDataImpl;
        _steamGetLobbyData = SteamGetLobbyDataImpl;
        _steamSendP2P = SteamSendP2PImpl;
        _steamIsP2PAvailable = SteamIsP2PAvailableImpl;
        _steamReadP2P = SteamReadP2PImpl;
        _steamAcceptP2P = SteamAcceptP2PImpl;
        _steamPollEvent = SteamPollEventImpl;
        _getPlayerPosition = GetPlayerPositionImpl;

        _configRegisterBool = ConfigRegisterBoolImpl;
        _configRegisterInt = ConfigRegisterIntImpl;
        _configRegisterFloat = ConfigRegisterFloatImpl;
        _configGetBool = ConfigGetBoolImpl;
        _configGetInt = ConfigGetIntImpl;
        _configGetFloat = ConfigGetFloatImpl;

        _spawnCharacter = SpawnCharacterImpl;
        _destroyEntity = DestroyEntityImpl;
        _setEntityPosition = SetEntityPositionImpl;
        _isEntityReady = IsEntityReadyImpl;
        _setEntityAnimation = SetEntityAnimationImpl;
        _getPrefabCount = GetPrefabCountImpl;
        _setEntityName = SetEntityNameImpl;
        _getPlayerCarryState = GetPlayerCarryStateImpl;
        _getPlayerCrouching = GetPlayerCrouchingImpl;
        _getPlayerSitting = GetPlayerSittingImpl;
        _setEntityCrouching = SetEntityCrouchingImpl;
        _setEntitySitting = SetEntitySittingImpl;
        _setEntityCarryAnim = SetEntityCarryAnimImpl;
        _createEntityCarryVisual = CreateEntityCarryVisualImpl;
        _destroyEntityCarryVisual = DestroyEntityCarryVisualImpl;
        _getDefaultSpawnPosition = GetDefaultSpawnPositionImpl;
        _warpLocalPlayer = WarpLocalPlayerImpl;
        _getEntityPosition = GetEntityPositionImpl;
        _addEntityCollider = AddEntityColliderImpl;
        _setEntityCarryTransform = SetEntityCarryTransformImpl;
        // v13
        _worldGetObjectCount = WorldGetObjectCountImpl;
        _worldGetObjectHashes = WorldGetObjectHashesImpl;
        _worldGetObjectState = WorldGetObjectStateImpl;
        _worldSpawnObject = WorldSpawnObjectImpl;
        _worldDestroyObject = WorldDestroyObjectImpl;
        _worldPlaceInRack = WorldPlaceInRackImpl;
        _worldRemoveFromRack = WorldRemoveFromRackImpl;
        _worldSetPower = WorldSetPowerImpl;
        _worldSetProperty = WorldSetPropertyImpl;
        _worldConnectCable = WorldConnectCableImpl;
        _worldDisconnectCable = WorldDisconnectCableImpl;
        _worldPickupObject = WorldPickupObjectImpl;
        _worldDropObject = WorldDropObjectImpl;
        _worldEnsureRackUIDs = WorldEnsureRackUIDsImpl;

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
            RegisterCustomEmployee = Marshal.GetFunctionPointerForDelegate(_registerCustomEmployee),
            IsCustomEmployeeHired = Marshal.GetFunctionPointerForDelegate(_isCustomEmployeeHired),
            FireCustomEmployee = Marshal.GetFunctionPointerForDelegate(_fireCustomEmployee),
            RegisterSalary = Marshal.GetFunctionPointerForDelegate(_registerSalary),
            ShowNotification = Marshal.GetFunctionPointerForDelegate(_showNotification),
            GetMoneyPerSecond = Marshal.GetFunctionPointerForDelegate(_getMoneyPerSecond),
            GetExpensesPerSecond = Marshal.GetFunctionPointerForDelegate(_getExpensesPerSecond),
            GetXpPerSecond = Marshal.GetFunctionPointerForDelegate(_getXpPerSecond),
            IsGamePaused = Marshal.GetFunctionPointerForDelegate(_isGamePaused2),
            SetGamePaused = Marshal.GetFunctionPointerForDelegate(_setGamePaused),
            GetDifficulty = Marshal.GetFunctionPointerForDelegate(_getDifficulty),
            TriggerSave = Marshal.GetFunctionPointerForDelegate(_triggerSave),
            SteamGetMyId = Marshal.GetFunctionPointerForDelegate(_steamGetMyId),
            SteamGetFriendName = Marshal.GetFunctionPointerForDelegate(_steamGetFriendName),
            SteamCreateLobby = Marshal.GetFunctionPointerForDelegate(_steamCreateLobby),
            SteamJoinLobby = Marshal.GetFunctionPointerForDelegate(_steamJoinLobby),
            SteamLeaveLobby = Marshal.GetFunctionPointerForDelegate(_steamLeaveLobby),
            SteamGetLobbyId = Marshal.GetFunctionPointerForDelegate(_steamGetLobbyId),
            SteamGetLobbyOwner = Marshal.GetFunctionPointerForDelegate(_steamGetLobbyOwner),
            SteamGetLobbyMemberCount = Marshal.GetFunctionPointerForDelegate(_steamGetLobbyMemberCount),
            SteamGetLobbyMemberByIndex = Marshal.GetFunctionPointerForDelegate(_steamGetLobbyMemberByIndex),
            SteamSetLobbyData = Marshal.GetFunctionPointerForDelegate(_steamSetLobbyData),
            SteamGetLobbyData = Marshal.GetFunctionPointerForDelegate(_steamGetLobbyData),
            SteamSendP2P = Marshal.GetFunctionPointerForDelegate(_steamSendP2P),
            SteamIsP2PAvailable = Marshal.GetFunctionPointerForDelegate(_steamIsP2PAvailable),
            SteamReadP2P = Marshal.GetFunctionPointerForDelegate(_steamReadP2P),
            SteamAcceptP2P = Marshal.GetFunctionPointerForDelegate(_steamAcceptP2P),
            SteamPollEvent = Marshal.GetFunctionPointerForDelegate(_steamPollEvent),
            GetPlayerPosition = Marshal.GetFunctionPointerForDelegate(_getPlayerPosition),
            ConfigRegisterBool = Marshal.GetFunctionPointerForDelegate(_configRegisterBool),
            ConfigRegisterInt = Marshal.GetFunctionPointerForDelegate(_configRegisterInt),
            ConfigRegisterFloat = Marshal.GetFunctionPointerForDelegate(_configRegisterFloat),
            ConfigGetBool = Marshal.GetFunctionPointerForDelegate(_configGetBool),
            ConfigGetInt = Marshal.GetFunctionPointerForDelegate(_configGetInt),
            ConfigGetFloat = Marshal.GetFunctionPointerForDelegate(_configGetFloat),
            SpawnCharacter = Marshal.GetFunctionPointerForDelegate(_spawnCharacter),
            DestroyEntity = Marshal.GetFunctionPointerForDelegate(_destroyEntity),
            SetEntityPosition = Marshal.GetFunctionPointerForDelegate(_setEntityPosition),
            IsEntityReady = Marshal.GetFunctionPointerForDelegate(_isEntityReady),
            SetEntityAnimation = Marshal.GetFunctionPointerForDelegate(_setEntityAnimation),
            GetPrefabCount = Marshal.GetFunctionPointerForDelegate(_getPrefabCount),
            SetEntityName = Marshal.GetFunctionPointerForDelegate(_setEntityName),
            GetPlayerCarryState = Marshal.GetFunctionPointerForDelegate(_getPlayerCarryState),
            GetPlayerCrouching = Marshal.GetFunctionPointerForDelegate(_getPlayerCrouching),
            GetPlayerSitting = Marshal.GetFunctionPointerForDelegate(_getPlayerSitting),
            SetEntityCrouching = Marshal.GetFunctionPointerForDelegate(_setEntityCrouching),
            SetEntitySitting = Marshal.GetFunctionPointerForDelegate(_setEntitySitting),
            SetEntityCarryAnim = Marshal.GetFunctionPointerForDelegate(_setEntityCarryAnim),
            CreateEntityCarryVisual = Marshal.GetFunctionPointerForDelegate(_createEntityCarryVisual),
            DestroyEntityCarryVisual = Marshal.GetFunctionPointerForDelegate(_destroyEntityCarryVisual),
            GetDefaultSpawnPosition = Marshal.GetFunctionPointerForDelegate(_getDefaultSpawnPosition),
            WarpLocalPlayer = Marshal.GetFunctionPointerForDelegate(_warpLocalPlayer),
            GetEntityPosition = Marshal.GetFunctionPointerForDelegate(_getEntityPosition),
            AddEntityCollider = Marshal.GetFunctionPointerForDelegate(_addEntityCollider),
            SetEntityCarryTransform = Marshal.GetFunctionPointerForDelegate(_setEntityCarryTransform),

            WorldGetObjectCount = Marshal.GetFunctionPointerForDelegate(_worldGetObjectCount),
            WorldGetObjectHashes = Marshal.GetFunctionPointerForDelegate(_worldGetObjectHashes),
            WorldGetObjectState = Marshal.GetFunctionPointerForDelegate(_worldGetObjectState),
            WorldSpawnObject = Marshal.GetFunctionPointerForDelegate(_worldSpawnObject),
            WorldDestroyObject = Marshal.GetFunctionPointerForDelegate(_worldDestroyObject),
            WorldPlaceInRack = Marshal.GetFunctionPointerForDelegate(_worldPlaceInRack),
            WorldRemoveFromRack = Marshal.GetFunctionPointerForDelegate(_worldRemoveFromRack),
            WorldSetPower = Marshal.GetFunctionPointerForDelegate(_worldSetPower),
            WorldSetProperty = Marshal.GetFunctionPointerForDelegate(_worldSetProperty),
            WorldConnectCable = Marshal.GetFunctionPointerForDelegate(_worldConnectCable),
            WorldDisconnectCable = Marshal.GetFunctionPointerForDelegate(_worldDisconnectCable),
            WorldPickupObject = Marshal.GetFunctionPointerForDelegate(_worldPickupObject),
            WorldDropObject = Marshal.GetFunctionPointerForDelegate(_worldDropObject),
            WorldEnsureRackUIDs = Marshal.GetFunctionPointerForDelegate(_worldEnsureRackUIDs),
        };

        _tablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<GameAPITable>());
        Marshal.StructureToPtr(_table, _tablePtr, false);
    }

    public IntPtr GetTablePointer() => _tablePtr;

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

    private static bool _netWatchEnabled;

    private void SetNetWatchEnabledImpl(uint value)
    {
        _netWatchEnabled = value != 0;
    }

    private uint IsNetWatchEnabledImpl() { return _netWatchEnabled ? 1u : 0u; }
    private uint GetNetWatchStatsImpl() { return 0; }


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


    private int RegisterCustomEmployeeImpl(IntPtr employeeId, IntPtr name, IntPtr description, float salary, float requiredReputation, uint confirmDialogs)
    {
        try
        {
            string id = Marshal.PtrToStringAnsi(employeeId) ?? "";
            string n = Marshal.PtrToStringAnsi(name) ?? "";
            string desc = Marshal.PtrToStringAnsi(description) ?? "";
            CrashLog.Log($"RegisterCustomEmployee: id={id}, name={n}, salary={salary}, rep={requiredReputation}, confirmDialogs={confirmDialogs}");
            return CustomEmployeeManager.Register(id, n, desc, salary, requiredReputation, confirmDialogs != 0);
        }
        catch (Exception ex)
        {
            _logger.Error("RegisterCustomEmployee: " + ex.Message);
            CrashLog.LogException("RegisterCustomEmployee", ex);
            return 0;
        }
    }

    private uint IsCustomEmployeeHiredImpl(IntPtr employeeId)
    {
        try
        {
            string id = Marshal.PtrToStringAnsi(employeeId) ?? "";
            return CustomEmployeeManager.IsHired(id) ? 1u : 0u;
        }
        catch { return 0; }
    }

    private int FireCustomEmployeeImpl(IntPtr employeeId)
    {
        try
        {
            string id = Marshal.PtrToStringAnsi(employeeId) ?? "";
            return CustomEmployeeManager.Fire(id);
        }
        catch { return 0; }
    }

    private int RegisterSalaryImpl(int monthlySalary)
    {
        try
        {
            var bs = BalanceSheet.instance;
            if (bs == null) return 0;
            bs.RegisterSalary(monthlySalary);
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("RegisterSalary", ex);
            return 0;
        }
    }


    private int ShowNotificationImpl(IntPtr message)
    {
        try
        {
            string msg = Marshal.PtrToStringAnsi(message) ?? "";
            var ui = StaticUIElements.instance;
            if (ui == null) return 0;
            ui.AddMeesageInField(msg);
            return 1;
        }
        catch (Exception ex) { CrashLog.LogException("ShowNotification", ex); return 0; }
    }

    private float GetMoneyPerSecondImpl()
    {
        try
        {
            var ui = StaticUIElements.instance;
            if (ui == null) return 0f;
            ui.CalculateRates(out float money, out float _, out float _);
            return money;
        }
        catch { return 0f; }
    }

    private float GetExpensesPerSecondImpl()
    {
        try
        {
            var ui = StaticUIElements.instance;
            if (ui == null) return 0f;
            ui.CalculateRates(out float _, out float _, out float expenses);
            return expenses;
        }
        catch { return 0f; }
    }

    private float GetXpPerSecondImpl()
    {
        try
        {
            var ui = StaticUIElements.instance;
            if (ui == null) return 0f;
            ui.CalculateRates(out float _, out float xp, out float _);
            return xp;
        }
        catch { return 0f; }
    }

    private uint IsGamePausedImpl()
    {
        try { return MainGameManager.instance?.isGamePaused == true ? 1u : 0u; }
        catch { return 0; }
    }

    private void SetGamePausedImpl(uint paused)
    {
        try
        {
            var mgr = MainGameManager.instance;
            if (mgr != null) mgr.isGamePaused = paused != 0;
        }
        catch (Exception ex) { CrashLog.LogException("SetGamePaused", ex); }
    }

    private int GetDifficultyImpl()
    {
        try { return MainGameManager.instance?.difficulty ?? -1; }
        catch { return -1; }
    }

    private int TriggerSaveImpl()
    {
        try { SaveSystem.SaveGame(); return 1; }
        catch (Exception ex) { CrashLog.LogException("TriggerSave", ex); return 0; }
    }


    private IntPtr GetSteamNetworking()
    {
        if (_steamNetworking == IntPtr.Zero)
            _steamNetworking = SteamAPI_SteamNetworking_v006();
        return _steamNetworking;
    }

    private IntPtr GetSteamUser()
    {
        if (_steamUser == IntPtr.Zero)
            _steamUser = SteamAPI_SteamUser_v023();
        return _steamUser;
    }

    private IntPtr GetSteamFriends()
    {
        if (_steamFriends == IntPtr.Zero)
            _steamFriends = SteamAPI_SteamFriends_v018();
        return _steamFriends;
    }

    private ulong SteamGetMyIdImpl()
    {
        try
        {
            var user = GetSteamUser();
            if (user == IntPtr.Zero) return 0;
            return SteamAPI_ISteamUser_GetSteamID(user);
        }
        catch (Exception ex) { CrashLog.LogException("SteamGetMyId", ex); return 0; }
    }

    private IntPtr SteamGetFriendNameImpl(ulong steamId)
    {
        try
        {
            var friends = GetSteamFriends();
            if (friends == IntPtr.Zero) return IntPtr.Zero;
            return SteamAPI_ISteamFriends_GetFriendPersonaName(friends, steamId);
        }
        catch (Exception ex) { CrashLog.LogException("SteamGetFriendName", ex); return IntPtr.Zero; }
    }

    private int SteamCreateLobbyImpl(uint lobbyType, uint maxPlayers) { return 0; }
    private int SteamJoinLobbyImpl(ulong lobbyId) { return 0; }
    private void SteamLeaveLobbyImpl() { }
    private ulong SteamGetLobbyIdImpl() { return 0; }
    private ulong SteamGetLobbyOwnerImpl() { return 0; }
    private uint SteamGetLobbyMemberCountImpl() { return 0; }
    private ulong SteamGetLobbyMemberByIndexImpl(uint index) { return 0; }
    private int SteamSetLobbyDataImpl(IntPtr key, IntPtr value) { return 0; }
    private IntPtr SteamGetLobbyDataImpl(IntPtr key) { return IntPtr.Zero; }

    private int SteamSendP2PImpl(ulong target, IntPtr data, uint len, uint reliable)
    {
        try
        {
            var networking = GetSteamNetworking();
            if (networking == IntPtr.Zero)
            {
                CrashLog.Log("[Steam] SendP2P: ISteamNetworking not available");
                return 0;
            }

            // k_EP2PSendUnreliable=0, k_EP2PSendReliable=2
            int sendType = reliable != 0 ? 2 : 0;
            bool ok = SteamAPI_ISteamNetworking_SendP2PPacket(networking, target, data, len, sendType, 0);
            if (!ok)
                CrashLog.Log($"[Steam] SendP2PPacket failed: target={target}, len={len}, reliable={reliable}");
            return ok ? 1 : 0;
        }
        catch (Exception ex) { CrashLog.LogException("SteamSendP2P", ex); return 0; }
    }

    private uint SteamIsP2PAvailableImpl(IntPtr outSize)
    {
        try
        {
            var networking = GetSteamNetworking();
            if (networking == IntPtr.Zero) return 0;

            bool available = SteamAPI_ISteamNetworking_IsP2PPacketAvailable(networking, out uint msgSize, 0);
            if (available && msgSize > 0)
            {
                if (outSize != IntPtr.Zero)
                    Marshal.WriteInt32(outSize, (int)msgSize);
                return 1;
            }
            return 0;
        }
        catch (Exception ex) { CrashLog.LogException("SteamIsP2PAvailable", ex); return 0; }
    }

    private uint SteamReadP2PImpl(IntPtr buf, uint bufLen, IntPtr outSender)
    {
        try
        {
            var networking = GetSteamNetworking();
            if (networking == IntPtr.Zero) return 0;

            bool ok = SteamAPI_ISteamNetworking_ReadP2PPacket(
                networking, buf, bufLen, out uint bytesRead, out ulong sender, 0);

            if (ok && bytesRead > 0)
            {
                if (outSender != IntPtr.Zero)
                    Marshal.WriteInt64(outSender, (long)sender);
                return bytesRead;
            }
            return 0;
        }
        catch (Exception ex) { CrashLog.LogException("SteamReadP2P", ex); return 0; }
    }

    private void SteamAcceptP2PImpl(ulong remote)
    {
        try
        {
            var networking = GetSteamNetworking();
            if (networking == IntPtr.Zero) return;

            bool ok = SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(networking, remote);
            CrashLog.Log($"[Steam] AcceptP2PSessionWithUser({remote}): {ok}");
        }
        catch (Exception ex) { CrashLog.LogException("SteamAcceptP2P", ex); }
    }

    private uint SteamPollEventImpl(IntPtr outType, IntPtr outData)
    {
        // TODO: implement event queue for lobby callbacks
        return 0;
    }

    private void GetPlayerPositionImpl(IntPtr outX, IntPtr outY, IntPtr outZ, IntPtr outRy)
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null || pm.playerGO == null) return;

            var pos = pm.playerGO.transform.position;
            var rot = pm.playerGO.transform.eulerAngles;

            if (outX != IntPtr.Zero) Marshal.Copy(new float[] { pos.x }, 0, outX, 1);
            if (outY != IntPtr.Zero) Marshal.Copy(new float[] { pos.y }, 0, outY, 1);
            if (outZ != IntPtr.Zero) Marshal.Copy(new float[] { pos.z }, 0, outZ, 1);
            if (outRy != IntPtr.Zero) Marshal.Copy(new float[] { rot.y }, 0, outRy, 1);
        }
        catch (Exception ex) { CrashLog.LogException("GetPlayerPosition", ex); }
    }

    private static uint ConfigRegisterBoolImpl(IntPtr modId, IntPtr key, IntPtr displayName, uint defaultValue, IntPtr description)
    {
        try
        {
            string mId = Marshal.PtrToStringAnsi(modId) ?? "";
            string k = Marshal.PtrToStringAnsi(key) ?? "";
            string dn = Marshal.PtrToStringAnsi(displayName) ?? k;
            string desc = Marshal.PtrToStringAnsi(description) ?? "";
            return ModConfigSystem.RegisterBool(mId, k, dn, defaultValue != 0, desc);
        }
        catch (Exception ex) { CrashLog.LogException("ConfigRegisterBoolImpl", ex); return 0; }
    }

    private static uint ConfigRegisterIntImpl(IntPtr modId, IntPtr key, IntPtr displayName, int defaultValue, int min, int max, IntPtr description)
    {
        try
        {
            string mId = Marshal.PtrToStringAnsi(modId) ?? "";
            string k = Marshal.PtrToStringAnsi(key) ?? "";
            string dn = Marshal.PtrToStringAnsi(displayName) ?? k;
            string desc = Marshal.PtrToStringAnsi(description) ?? "";
            return ModConfigSystem.RegisterInt(mId, k, dn, defaultValue, min, max, desc);
        }
        catch (Exception ex) { CrashLog.LogException("ConfigRegisterIntImpl", ex); return 0; }
    }

    private static uint ConfigRegisterFloatImpl(IntPtr modId, IntPtr key, IntPtr displayName, float defaultValue, float min, float max, IntPtr description)
    {
        try
        {
            string mId = Marshal.PtrToStringAnsi(modId) ?? "";
            string k = Marshal.PtrToStringAnsi(key) ?? "";
            string dn = Marshal.PtrToStringAnsi(displayName) ?? k;
            string desc = Marshal.PtrToStringAnsi(description) ?? "";
            return ModConfigSystem.RegisterFloat(mId, k, dn, defaultValue, min, max, desc);
        }
        catch (Exception ex) { CrashLog.LogException("ConfigRegisterFloatImpl", ex); return 0; }
    }

    private static uint ConfigGetBoolImpl(IntPtr modId, IntPtr key)
    {
        try
        {
            string mId = Marshal.PtrToStringAnsi(modId) ?? "";
            string k = Marshal.PtrToStringAnsi(key) ?? "";
            return ModConfigSystem.GetBool(mId, k);
        }
        catch (Exception ex) { CrashLog.LogException("ConfigGetBoolImpl", ex); return 0xFFFFFFFF; }
    }

    private static int ConfigGetIntImpl(IntPtr modId, IntPtr key)
    {
        try
        {
            string mId = Marshal.PtrToStringAnsi(modId) ?? "";
            string k = Marshal.PtrToStringAnsi(key) ?? "";
            return ModConfigSystem.GetInt(mId, k);
        }
        catch (Exception ex) { CrashLog.LogException("ConfigGetIntImpl", ex); return 0; }
    }

    private static float ConfigGetFloatImpl(IntPtr modId, IntPtr key)
    {
        try
        {
            string mId = Marshal.PtrToStringAnsi(modId) ?? "";
            string k = Marshal.PtrToStringAnsi(key) ?? "";
            return ModConfigSystem.GetFloat(mId, k);
        }
        catch (Exception ex) { CrashLog.LogException("ConfigGetFloatImpl", ex); return 0f; }
    }

    private static uint SpawnCharacterImpl(uint prefabIdx, float x, float y, float z, float rotY, IntPtr name)
    {
        try
        {
            string n = Marshal.PtrToStringAnsi(name) ?? "Entity";
            return EntityManager.SpawnCharacter(prefabIdx, x, y, z, rotY, n);
        }
        catch (Exception ex) { CrashLog.LogException("SpawnCharacterImpl", ex); return 0; }
    }

    private static void DestroyEntityImpl(uint entityId)
    {
        try { EntityManager.DestroyEntity(entityId); }
        catch (Exception ex) { CrashLog.LogException("DestroyEntityImpl", ex); }
    }

    private static void SetEntityPositionImpl(uint entityId, float x, float y, float z, float rotY)
    {
        try { EntityManager.SetPosition(entityId, x, y, z, rotY); }
        catch (Exception ex) { CrashLog.LogException("SetEntityPositionImpl", ex); }
    }

    private static uint IsEntityReadyImpl(uint entityId)
    {
        try { return EntityManager.IsEntityReady(entityId) ? 1u : 0u; }
        catch (Exception ex) { CrashLog.LogException("IsEntityReadyImpl", ex); return 0; }
    }

    private static void SetEntityAnimationImpl(uint entityId, float speed, uint isWalking)
    {
        try { EntityManager.SetAnimation(entityId, speed, isWalking != 0); }
        catch (Exception ex) { CrashLog.LogException("SetEntityAnimationImpl", ex); }
    }

    private static uint GetPrefabCountImpl()
    {
        try { return EntityManager.GetPrefabCount(); }
        catch (Exception ex) { CrashLog.LogException("GetPrefabCountImpl", ex); return 0; }
    }

    private static void SetEntityNameImpl(uint entityId, IntPtr name)
    {
        try
        {
            string n = Marshal.PtrToStringAnsi(name) ?? "";
            EntityManager.SetEntityName(entityId, n);
        }
        catch (Exception ex) { CrashLog.LogException("SetEntityNameImpl", ex); }
    }

    private void GetPlayerCarryStateImpl(IntPtr outObjectInHand, IntPtr outNumObjects)
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null) return;
            uint objInHand = (uint)(int)pm.objectInHand;
            uint numObj = (uint)pm.numberOfObjectsInHand;
            if (outObjectInHand != IntPtr.Zero) Marshal.Copy(new int[] { (int)objInHand }, 0, outObjectInHand, 1);
            if (outNumObjects != IntPtr.Zero) Marshal.Copy(new int[] { (int)numObj }, 0, outNumObjects, 1);
        }
        catch (Exception ex) { CrashLog.LogException("GetPlayerCarryStateImpl", ex); }
    }

    private static uint GetPlayerCrouchingImpl()
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null || pm.fpc == null) return 0;
            return pm.fpc.m_isCrouching ? 1u : 0u;
        }
        catch (Exception ex) { CrashLog.LogException("GetPlayerCrouchingImpl", ex); return 0; }
    }

    private static uint GetPlayerSittingImpl()
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null || pm.fpc == null) return 0;
            return pm.fpc.m_IsSitting ? 1u : 0u;
        }
        catch (Exception ex) { CrashLog.LogException("GetPlayerSittingImpl", ex); return 0; }
    }

    private static void SetEntityCrouchingImpl(uint entityId, uint isCrouching)
    {
        try { EntityManager.SetCrouching(entityId, isCrouching != 0); }
        catch (Exception ex) { CrashLog.LogException("SetEntityCrouchingImpl", ex); }
    }

    private static void SetEntitySittingImpl(uint entityId, uint isSitting)
    {
        try { EntityManager.SetSitting(entityId, isSitting != 0); }
        catch (Exception ex) { CrashLog.LogException("SetEntitySittingImpl", ex); }
    }

    private static void SetEntityCarryAnimImpl(uint entityId, uint isCarrying)
    {
        try { EntityManager.SetCarryAnim(entityId, isCarrying != 0); }
        catch (Exception ex) { CrashLog.LogException("SetEntityCarryAnimImpl", ex); }
    }

    private static void CreateEntityCarryVisualImpl(uint entityId, uint objectInHandType)
    {
        try { EntityManager.CreateCarryVisual(entityId, objectInHandType); }
        catch (Exception ex) { CrashLog.LogException("CreateEntityCarryVisualImpl", ex); }
    }

    private static void DestroyEntityCarryVisualImpl(uint entityId)
    {
        try { EntityManager.DestroyCarryVisual(entityId); }
        catch (Exception ex) { CrashLog.LogException("DestroyEntityCarryVisualImpl", ex); }
    }

    private void GetDefaultSpawnPositionImpl(IntPtr outX, IntPtr outY, IntPtr outZ)
    {
        try
        {
            if (outX != IntPtr.Zero) Marshal.Copy(new float[] { 5f }, 0, outX, 1);
            if (outY != IntPtr.Zero) Marshal.Copy(new float[] { 1f }, 0, outY, 1);
            if (outZ != IntPtr.Zero) Marshal.Copy(new float[] { -24f }, 0, outZ, 1);
            CrashLog.Log("[GameAPI] Default spawn: (5, 1, -24)");
        }
        catch (Exception ex) { CrashLog.LogException("GetDefaultSpawnPosition", ex); }
    }

    private void WarpLocalPlayerImpl(float x, float y, float z)
    {
        try
        {
            var pm = PlayerManager.instance;
            if (pm == null || pm.playerClass == null || pm.playerGO == null) return;

            var pos = new Vector3(x, y, z);
            var rot = pm.playerGO.transform.rotation;
            pm.playerClass.WarpPlayer(pos, rot);
            CrashLog.Log($"[GameAPI] Warped local player to ({x:F1},{y:F1},{z:F1})");
        }
        catch (Exception ex) { CrashLog.LogException("WarpLocalPlayer", ex); }
    }

    private static uint GetEntityPositionImpl(uint entityId, IntPtr outX, IntPtr outY, IntPtr outZ)
    {
        try
        {
            var pos = EntityManager.GetEntityPosition(entityId);
            if (pos == null) return 0;
            var p = pos.Value;
            if (outX != IntPtr.Zero) Marshal.Copy(new float[] { p.x }, 0, outX, 1);
            if (outY != IntPtr.Zero) Marshal.Copy(new float[] { p.y }, 0, outY, 1);
            if (outZ != IntPtr.Zero) Marshal.Copy(new float[] { p.z }, 0, outZ, 1);
            return 1;
        }
        catch (Exception ex) { CrashLog.LogException("GetEntityPositionImpl", ex); return 0; }
    }

    private static void AddEntityColliderImpl(uint entityId)
    {
        try { EntityManager.AddEntityCollider(entityId); }
        catch (Exception ex) { CrashLog.LogException("AddEntityColliderImpl", ex); }
    }

    private static void SetEntityCarryTransformImpl(uint entityId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ)
    {
        try { EntityManager.SetEntityCarryTransform(entityId, posX, posY, posZ, rotX, rotY, rotZ); }
        catch (Exception ex) { CrashLog.LogException("SetEntityCarryTransformImpl", ex); }
    }

    // ── v13: World Object Sync ────────────────────────────────────────

    static string ReadUtf8(IntPtr ptr, uint len)
    {
        if (ptr == IntPtr.Zero || len == 0) return "";
        byte[] buf = new byte[len];
        Marshal.Copy(ptr, buf, 0, (int)len);
        int end = Array.IndexOf(buf, (byte)0);
        if (end < 0) end = (int)len;
        return System.Text.Encoding.UTF8.GetString(buf, 0, end).Trim();
    }

    uint WorldGetObjectCountImpl()
    {
        // Phase 4 stub
        return 0;
    }

    uint WorldGetObjectHashesImpl(IntPtr buf, uint maxCount)
    {
        // Phase 4 stub
        return 0;
    }

    uint WorldGetObjectStateImpl(IntPtr id, uint idLen, IntPtr buf, uint bufMax)
    {
        // Phase 4 stub
        return 0;
    }

    int WorldSpawnObjectImpl(byte objectType, int prefabId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, IntPtr outId, uint outMax)
    {
        try
        {
            string desiredId = null;
            if (outId != IntPtr.Zero && outMax > 0)
            {
                byte firstByte = Marshal.ReadByte(outId);
                if (firstByte != 0)
                {
                    desiredId = ReadUtf8(outId, outMax);
                }
            }

            CrashLog.Log($"[WorldSync] SpawnObject: type={objectType}, prefab={prefabId}, desiredId='{desiredId ?? "(none)"}', pos=({x:F1},{y:F1},{z:F1})");

            var mgr = Il2Cpp.MainGameManager.instance;
            if (mgr == null)
            {
                CrashLog.Log("[WorldSync] SpawnObject: MainGameManager is null");
                return 0;
            }

            UnityEngine.GameObject prefab = null;

            try
            {
                if (mgr.serverPrefabs != null && prefabId >= 0 && prefabId < mgr.serverPrefabs.Count)
                {
                    prefab = mgr.serverPrefabs[prefabId];
                }
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] SpawnObject: serverPrefabs lookup failed: {ex.Message}");
            }

            if (prefab == null && objectType == 4)
            {
                try
                {
                    if (mgr.switchesPrefabs != null && prefabId >= 0 && prefabId < mgr.switchesPrefabs.Count)
                    {
                        prefab = mgr.switchesPrefabs[prefabId];
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.Log($"[WorldSync] SpawnObject: switchesPrefabs lookup failed: {ex.Message}");
                }
            }

            if (prefab == null)
            {
                CrashLog.Log($"[WorldSync] SpawnObject: no prefab found for type={objectType} prefabId={prefabId}");
                return 0;
            }

            var pos = new UnityEngine.Vector3(x, y, z);
            var rot = new UnityEngine.Quaternion(rotX, rotY, rotZ, rotW);
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
            if (go == null)
            {
                CrashLog.Log("[WorldSync] SpawnObject: Instantiate returned null");
                return 0;
            }

            try
            {
                if (mgr.parentUsableObjects != null)
                    go.transform.SetParent(mgr.parentUsableObjects, true);
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] SpawnObject: parenting failed (non-fatal): {ex.Message}");
            }

            string resultId = go.name;

            try
            {
                var serverComp = go.GetComponent<Il2Cpp.Server>();
                if (serverComp != null)
                {
                    if (!string.IsNullOrEmpty(desiredId))
                    {
                        serverComp.ServerID = desiredId;
                        resultId = desiredId;
                        CrashLog.Log($"[WorldSync] SpawnObject: set ServerID to '{desiredId}'");
                    }
                    else
                    {
                        resultId = serverComp.ServerID ?? go.name;
                    }

                    try
                    {
                        var rb = serverComp.rb;
                        if (rb != null)
                        {
                            rb.isKinematic = false;
                            rb.useGravity = true;
                            rb.velocity = UnityEngine.Vector3.zero;
                            rb.angularVelocity = UnityEngine.Vector3.zero;
                            rb.WakeUp();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] SpawnObject: server setup failed: {ex.Message}");
            }

            if (outId != IntPtr.Zero && outMax > 0)
            {
                for (uint i = 0; i < outMax && i < 128; i++)
                    Marshal.WriteByte(outId, (int)i, 0);

                var bytes = System.Text.Encoding.UTF8.GetBytes(resultId);
                int copyLen = Math.Min(bytes.Length, (int)outMax - 1);
                Marshal.Copy(bytes, 0, outId, copyLen);
                Marshal.WriteByte(outId, copyLen, 0);
            }

            try { Patch_ComputerShop_SpawnPhysicalItem.RegisterRemoteSpawn(go.GetInstanceID()); }
            catch { }

            CrashLog.Log($"[WorldSync] SpawnObject: created '{resultId}' (type={objectType}, prefab={prefabId}) OK");
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("WorldSpawnObjectImpl", ex);
            return 0;
        }
    }

    int WorldDestroyObjectImpl(IntPtr id, uint idLen)
    {
        // Phase 3 stub
        string objId = ReadUtf8(id, idLen);
        CrashLog.Log($"[WorldSync] DestroyObject stub: id={objId}");
        return 0;
    }

    int WorldPlaceInRackImpl(IntPtr id, uint idLen, int rackUid)
    {
        try
        {
            string serverId = ReadUtf8(id, idLen);
            CrashLog.Log($"[WorldSync] PlaceInRack: id={serverId}, rackUid={rackUid}");

            // ── 1. Find the server by its ServerID ──────────────────────────
            var servers = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.Server>();
            Il2Cpp.Server targetServer = null;
            foreach (var srv in servers)
            {
                try
                {
                    // Skip prefabs / assets (only want scene objects)
                    if (srv.gameObject.scene.name == null) continue;
                    if (srv.ServerID == serverId)
                    {
                        targetServer = srv;
                        break;
                    }
                }
                catch { }
            }

            if (targetServer == null)
            {
                // Count only scene servers for the log
                int sceneCount = 0;
                foreach (var srv in servers)
                {
                    try
                    {
                        if (srv.gameObject.scene.name == null) continue;
                        sceneCount++;
                    }
                    catch { }
                }
                CrashLog.Log($"[WorldSync] PlaceInRack: server '{serverId}' not found among {sceneCount} servers");
                foreach (var srv in servers)
                {
                    try
                    {
                        if (srv.gameObject.scene.name == null) continue;
                        CrashLog.Log($"[WorldSync]   known server: '{srv.ServerID}' active={srv.gameObject.activeSelf}");
                    }
                    catch { }
                }
                return 0;
            }

            Il2Cpp.RackPosition targetRackPos = null;

            var rackPositions = UnityEngine.Object.FindObjectsOfType<Il2Cpp.RackPosition>();
            foreach (var rp in rackPositions)
            {
                try
                {
                    if (rp.rackPosGlobalUID == rackUid)
                    {
                        targetRackPos = rp;
                        break;
                    }
                }
                catch { }
            }

            if (targetRackPos == null)
            {
                CrashLog.Log($"[WorldSync] PlaceInRack: UID={rackUid} not found among {rackPositions.Count} positions — force-reassigning UIDs...");
                GameHooks.EnsureAllRackPositionUIDs();

                rackPositions = UnityEngine.Object.FindObjectsOfType<Il2Cpp.RackPosition>();
                foreach (var rp in rackPositions)
                {
                    try
                    {
                        if (rp.rackPosGlobalUID == rackUid)
                        {
                            targetRackPos = rp;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (targetRackPos == null)
            {
                int minUid = int.MaxValue, maxUid = int.MinValue;
                foreach (var rp in rackPositions)
                {
                    try
                    {
                        int u = rp.rackPosGlobalUID;
                        if (u < minUid) minUid = u;
                        if (u > maxUid) maxUid = u;
                    }
                    catch { }
                }
                CrashLog.Log($"[WorldSync] PlaceInRack: UID={rackUid} still not found. Existing UID range: [{minUid}..{maxUid}] across {rackPositions.Count} positions");
                return 0;
            }

            Il2Cpp.Rack rack = targetRackPos.rack;
            if (rack == null)
            {
                CrashLog.Log($"[WorldSync] PlaceInRack: rackPosition UID={rackUid} has no parent Rack reference");
                return 0;
            }

            CrashLog.Log($"[WorldSync] PlaceInRack: found server '{serverId}' and rackPos UID={rackUid} (index={targetRackPos.positionIndex})");

            // Reactivate if it was deactivated by a pickup
            if (!targetServer.gameObject.activeSelf)
            {
                targetServer.gameObject.SetActive(true);
                CrashLog.Log($"[WorldSync] PlaceInRack: reactivated server '{serverId}' for rack install");
            }

            try
            {
                var saveData = new Il2Cpp.ServerSaveData();
                saveData.serverID = serverId;
                saveData.rackPositionUID = rackUid;
                saveData.serverType = targetServer.serverType;
                saveData.position = targetRackPos.transform.position;
                saveData.rotation = targetRackPos.transform.rotation;
                try { saveData.isOn = targetServer.isOn; } catch { saveData.isOn = false; }
                try { saveData.isBroken = targetServer.isBroken; } catch { saveData.isBroken = false; }

                targetServer.ServerInsertedInRack(saveData);
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] PlaceInRack: ServerInsertedInRack call failed (non-fatal): {ex.Message}");
            }

            // ── 4. Physical placement: parent + position the server ─────────
            try
            {
                targetServer.transform.SetParent(targetRackPos.transform, false);
                targetServer.transform.localPosition = UnityEngine.Vector3.zero;
                targetServer.transform.localRotation = UnityEngine.Quaternion.identity;
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] PlaceInRack: transform parenting failed: {ex.Message}");
            }

            // ── 5. Update server bookkeeping fields ─────────────────────────
            try
            {
                targetServer.rackPositionUID = rackUid;
                targetServer.currentRackPosition = targetRackPos;
                targetServer.objectInHands = false;
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] PlaceInRack: server field update failed: {ex.Message}");
            }

            // ── 6. Disable physics so the server doesn't fall ───────────────
            try
            {
                var rb = targetServer.rb;
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.velocity = UnityEngine.Vector3.zero;
                    rb.angularVelocity = UnityEngine.Vector3.zero;
                }
            }
            catch (Exception ex)
            {
                CrashLog.Log($"[WorldSync] PlaceInRack: rigidbody disable failed: {ex.Message}");
            }

            try
            {
                int sizeInU = 1;
                try { sizeInU = targetServer.sizeInU; } catch { }
                if (sizeInU <= 0) sizeInU = 1;
                Patch_Rack_MarkPositionAsUsed.SuppressEvents = true;
                try
                {
                    rack.MarkPositionAsUsed(targetRackPos.positionIndex, sizeInU);
                }
                finally
                {
                    Patch_Rack_MarkPositionAsUsed.SuppressEvents = false;
                }
                CrashLog.Log($"[WorldSync] PlaceInRack: marked position index={targetRackPos.positionIndex} sizeInU={sizeInU} as used");
            }
            catch (Exception ex)
            {
                Patch_Rack_MarkPositionAsUsed.SuppressEvents = false;
                CrashLog.Log($"[WorldSync] PlaceInRack: MarkPositionAsUsed failed: {ex.Message}");
            }

            CrashLog.Log($"[WorldSync] PlaceInRack: '{serverId}' fully installed at UID={rackUid} OK");
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("WorldPlaceInRackImpl", ex);
            return 0;
        }
    }

    int WorldRemoveFromRackImpl(IntPtr id, uint idLen)
    {
        // Phase 3 stub
        string objId = ReadUtf8(id, idLen);
        CrashLog.Log($"[WorldSync] RemoveFromRack stub: id={objId}");
        return 0;
    }

    int WorldSetPowerImpl(IntPtr id, uint idLen, byte isOn)
    {
        // Phase 3 stub
        string objId = ReadUtf8(id, idLen);
        CrashLog.Log($"[WorldSync] SetPower stub: id={objId}, on={isOn}");
        return 0;
    }

    int WorldSetPropertyImpl(IntPtr id, uint idLen, IntPtr key, uint keyLen, IntPtr val, uint valLen)
    {
        // Phase 3 stub
        return 0;
    }

    int WorldConnectCableImpl(int cableId, byte startType, float sx, float sy, float sz, IntPtr startDevice, uint startDeviceLen, byte endType, float ex, float ey, float ez, IntPtr endDevice, uint endDeviceLen)
    {
        // Phase 3 stub
        CrashLog.Log($"[WorldSync] ConnectCable stub: cableId={cableId}");
        return 0;
    }

    int WorldDisconnectCableImpl(int cableId)
    {
        // Phase 3 stub
        CrashLog.Log($"[WorldSync] DisconnectCable stub: cableId={cableId}");
        return 0;
    }

    int WorldPickupObjectImpl(IntPtr id, uint idLen)
    {
        try
        {
            string objId = ReadUtf8(id, idLen);
            CrashLog.Log($"[WorldSync] PickupObject: id={objId}");

            // Find the server by ID (including inactive objects)
            var allServers = UnityEngine.Resources.FindObjectsOfTypeAll<Server>();
            foreach (var srv in allServers)
            {
                try
                {
                    if (srv.gameObject.scene.name == null) continue; // skip prefabs
                    string sid = srv.ServerID ?? "";
                    if (sid == objId)
                    {
                        srv.gameObject.SetActive(false);
                        CrashLog.Log($"[WorldSync] PickupObject: deactivated server '{objId}'");
                        return 1;
                    }
                }
                catch { }
            }

            var allUsable = UnityEngine.Resources.FindObjectsOfTypeAll<UsableObject>();
            foreach (var uo in allUsable)
            {
                try
                {
                    if (uo.gameObject.scene.name == null) continue; // skip prefabs
                    string uoId = $"{uo.gameObject.name}_{uo.GetInstanceID()}";
                    if (uoId == objId)
                    {
                        uo.gameObject.SetActive(false);
                        CrashLog.Log($"[WorldSync] PickupObject: deactivated '{objId}'");
                        return 1;
                    }
                }
                catch { }
            }

            CrashLog.Log($"[WorldSync] PickupObject: object '{objId}' not found");
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("WorldPickupObjectImpl", ex);
            return 0;
        }
    }

    int WorldDropObjectImpl(IntPtr id, uint idLen, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW)
    {
        try
        {
            string objId = ReadUtf8(id, idLen);
            CrashLog.Log($"[WorldSync] DropObject: id={objId}, pos=({x:F1},{y:F1},{z:F1})");

            var pos = new UnityEngine.Vector3(x, y, z);
            var rot = new UnityEngine.Quaternion(rotX, rotY, rotZ, rotW);

            // Find the server by ID (may be deactivated, so search ALL including inactive)
            // FindObjectsOfType doesn't find inactive objects, so we need to search differently
            var allServers = UnityEngine.Resources.FindObjectsOfTypeAll<Server>();
            foreach (var srv in allServers)
            {
                try
                {
                    // Skip prefabs / assets (only want scene objects)
                    if (srv.gameObject.scene.name == null) continue;

                    string sid = srv.ServerID ?? "";
                    if (sid == objId)
                    {
                        // Reactivate and position
                        srv.gameObject.SetActive(true);
                        srv.transform.position = pos;
                        srv.transform.rotation = rot;

                        // Unparent from any rack/player and re-parent to world
                        var mgr = MainGameManager.instance;
                        if (mgr != null && mgr.parentUsableObjects != null)
                            srv.transform.SetParent(mgr.parentUsableObjects, true);

                        // Enable physics so it falls naturally
                        var rb = srv.GetComponent<UnityEngine.Rigidbody>();
                        if (rb != null)
                        {
                            rb.isKinematic = false;
                            rb.useGravity = true;
                            rb.WakeUp();
                        }

                        CrashLog.Log($"[WorldSync] DropObject: reactivated server '{objId}' at ({x:F1},{y:F1},{z:F1})");
                        return 1;
                    }
                }
                catch { }
            }

            // Try UsableObjects by name_instanceId pattern
            var allUsable = UnityEngine.Resources.FindObjectsOfTypeAll<UsableObject>();
            foreach (var uo in allUsable)
            {
                try
                {
                    if (uo.gameObject.scene.name == null) continue;

                    string uoId = $"{uo.gameObject.name}_{uo.GetInstanceID()}";
                    if (uoId == objId)
                    {
                        uo.gameObject.SetActive(true);
                        uo.transform.position = pos;
                        uo.transform.rotation = rot;

                        var mgr = MainGameManager.instance;
                        if (mgr != null && mgr.parentUsableObjects != null)
                            uo.transform.SetParent(mgr.parentUsableObjects, true);

                        var rb = uo.GetComponent<UnityEngine.Rigidbody>();
                        if (rb != null)
                        {
                            rb.isKinematic = false;
                            rb.useGravity = true;
                            rb.WakeUp();
                        }

                        CrashLog.Log($"[WorldSync] DropObject: reactivated '{objId}' at ({x:F1},{y:F1},{z:F1})");
                        return 1;
                    }
                }
                catch { }
            }

            CrashLog.Log($"[WorldSync] DropObject: object '{objId}' not found");
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("WorldDropObjectImpl", ex);
            return 0;
        }
    }

    int WorldEnsureRackUIDsImpl()
    {
        try
        {
            return GameHooks.EnsureAllRackPositionUIDs();
        }
        catch (Exception ex)
        {
            CrashLog.LogException("WorldEnsureRackUIDsImpl", ex);
            return 0;
        }
    }

    public void Dispose()
    {
        if (_tablePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_tablePtr); _tablePtr = IntPtr.Zero; }
        if (_currentScenePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_currentScenePtr); _currentScenePtr = IntPtr.Zero; }
        if (_friendNamePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_friendNamePtr); _friendNamePtr = IntPtr.Zero; }
        if (_lobbyDataPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_lobbyDataPtr); _lobbyDataPtr = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
}
