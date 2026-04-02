# Data Center Game — API Reference for Modding (FFI via Il2Cpp)

All types live in the `Il2Cpp` namespace. Assembly: `Assembly-CSharp.dll`.

---

## 1. `BalanceSheet` (extends `MonoBehaviour`)

Singleton financial tracker. Manages revenue, expenses, penalties per customer per month.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `BalanceSheet` | `instance` | Singleton accessor |

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `List<MonthlySnapshot>` | `history` | Historical monthly financial snapshots |
| `Dictionary<int, CustomerRecord>` | `currentRecords` | Live records keyed by customer ID |
| `float` | `totalMonthlySalary` | Sum of all technician salaries |
| `float` | `currentSalaryExpense` | Running salary expense this period |
| `List<GameObject>` | `activeRows` | UI rows currently shown |
| `Coroutine` | `trackFinancesCoroutine` | Running finance tracking coroutine |

### Methods
| Signature | Notes |
|-----------|-------|
| `CustomerRecord GetOrCreateRecord(CustomerItem item)` | Get or lazily create a record for a customer |
| `void RegisterSalary(int monthlySalary)` | Register a technician's salary |
| `int CountFailingApps(CustomerBase cb)` | Count apps failing SLA for a customer base |
| `void SaveSnapshot(int month, DateTime snapshotTime)` | Freeze current month into history |
| `MonthlySnapshot GetLatestSnapshot()` | Get most recent monthly snapshot |
| `void FillInBalanceSheet()` | Refresh the balance sheet UI |
| `void AddRow(string name, float revenue, float penalties, float total, Sprite logo = null)` | Add a display row |
| `void AddSalaryRow(float salaryExpense)` | Add the salary expense row |

### Nested: `BalanceSheet.CustomerRecord`
| Type | Name | Notes |
|------|------|-------|
| `int` | `customerID` | |
| `string` | `customerName` | |
| `Sprite` | `customerLogo` | |
| `float` | `revenue` | Gross revenue from this customer |
| `float` | `penalties` | SLA penalties incurred |
| `float` | `Total` | **Property (getter only):** `revenue - penalties` |

### Nested: `BalanceSheet.MonthlySnapshot`
| Type | Name | Notes |
|------|------|-------|
| `int` | `month` | Month index |
| `int` | `day` | Day within month |
| `List<CustomerRecord>` | `records` | Per-customer breakdown |
| `float` | `salaryExpense` | Technician salaries this month |
| `float` | `TotalRevenue` | **Property (getter):** sum of all customer revenues |
| `float` | `TotalPenalties` | **Property (getter):** sum of all penalties |
| `float` | `GrandTotal` | **Property (getter):** `TotalRevenue - TotalPenalties - salaryExpense` |

---

## 2. `Technician` (extends `MonoBehaviour`)

Represents a single technician NPC that can repair broken servers/switches.

### Enum: `Technician.TechnicianState`
| Value | Meaning |
|-------|---------|
| `Idle` | Standing around |
| `GoingForNewServer` | Walking to pick up a new server |
| `BringingNewServer` | Carrying a new server to the rack |
| `GoingBackWithOldServer` | Carrying a broken server to dumpster |
| `EndingHisWork` | Finishing up |

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `int` | `technicianID` | Unique ID |
| `string` | `technicianName` | Display name |
| `int` | `salary` | Monthly salary cost |
| `TechnicianState` | `currentState` | Current state machine state |
| `bool` | `isBusy` | Whether actively on a job |
| `Server` | `server` | Server currently being worked on |
| `Transform` | `transformIdle` | Idle position |
| `Transform` | `transformContainer` | Container/storage position |
| `Transform` | `transformDumpster` | Dumpster position |
| `Transform` | `transformInHandPosition` | Position for item in hand |
| `Transform` | `transformDeviceSpawnPosition` | Where new devices appear |
| `Transform` | `positionOfDeviceToBeReplaced` | Target rack slot |
| `TwoBoneIKConstraint` | `leftHandIK` / `rightHandIK` | IK constraints for animation |
| `Transform` | `leftHandTarget` / `rightHandTarget` | IK hand targets |

### Methods
| Signature | Notes |
|-----------|-------|
| `void AssignJob(TechnicianManager.RepairJob job)` | Assign a repair job to this technician |
| `void RepairDevice()` | Execute the repair action |
| `void RotateTowardsGoal(Vector3 goal)` | Rotate to face a position |
| `void PositionHandTargetsOnDevice(GameObject device)` | Position IK targets on a device |

---

## 3. `TechnicianManager` (extends `MonoBehaviour`)

Singleton that manages the pool of technicians and dispatch queue.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `TechnicianManager` | `instance` | Singleton accessor |
| `float` | `DISPATCH_INTERVAL` | Seconds between dispatch ticks |

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `Transform` | `transformContainer` | Shared container position |
| `Transform` | `transformDumpster` | Shared dumpster position |
| `Transform` | `transformDeviceSpawnPosition` | Shared spawn position |
| `int` | `lastAssignedIndex` | Round-robin index |
| `float` | `lastDispatchTime` | Timestamp of last dispatch |
| `Coroutine` | `dispatchCoroutine` | Running dispatch coroutine |
| `int` | `QueuedJobCount` | **Property:** number of jobs waiting |

### Methods
| Signature | Notes |
|-----------|-------|
| `void AddTechnician(Technician technician)` | Register a new technician |
| `void SendTechnician(NetworkSwitch nswitch, Server server)` | Dispatch a tech to repair a device |
| `void RequestNextJob(Technician technician)` | Technician requests another job |
| `void EnqueueDispatch(RepairJob job)` | Add a job to the queue |
| `bool IsDeviceAlreadyAssigned(NetworkSwitch ns, Server s)` | Check if a device has a tech en route |
| `void RestoreJobQueue(List<RepairJobSaveData> savedJobs)` | Restore jobs from save |
| `void FireTechnician(int technicianID)` | Remove a technician |

### Nested: `TechnicianManager.RepairJob`
| Type | Name | Notes |
|------|------|-------|
| `Technician` | `assignedTechnician` | |
| `string` | `DeviceName` | |

---

## 4. `MainGameManager` (extends `MonoBehaviour`)

Central singleton — game state, customer management, prefabs, UI canvases.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `MainGameManager` | `instance` | Singleton accessor |
| `PlayerManager.OnBuyingWall` | `onBuyingWallEvent` | Event fired when a wall is purchased |

### Instance Properties / Fields — Game State
| Type | Name | Notes |
|------|------|-------|
| `int` | `difficulty` | Game difficulty level |
| `bool` | `isGamePaused` | Is the game currently paused |
| `bool` | `isPauseMenuDisallowed` | Pause menu locked out |
| `bool` | `isPlayerCameraDisallowed` | Camera control locked |
| `bool` | `isAllowedToSave` | Can save right now |
| `bool` | `loadingFirstTime` | True during initial load |
| `string` | `languageXMLPath` | Path to localization XML |
| `bool` | `isIPHintHidden` | Whether IP hints are hidden |
| `float` | `xpGainMultiplier` | XP multiplier |
| `float` | `wallPrice` | Current wall purchase price |
| `float` | `autoSaveIntervalMinutes` | Auto-save interval |
| `bool` | `autoSaveEnabled` | Auto-save toggle |

### Instance Properties / Fields — Customer Data
| Type | Name | Notes |
|------|------|-------|
| `Il2CppReferenceArray<CustomerCard>` | `customerCards` | UI cards for customer choice |
| `Il2CppReferenceArray<CustomerItem>` | `customerItems` | All possible customers |
| `Il2CppReferenceArray<CustomerBase>` | `customerBases` | Active customer base instances |
| `Il2CppReferenceArray<CustomerItem>` | `chosenCustomerItems` | Currently chosen customers |
| `List<int>` | `availableCustomerIndices` | Indices of available customers |
| `List<string>` | `availableSubnets` | Free subnets for assignment |
| `List<int>` | `existingCustomerIDs` | IDs of active customers |
| `List<Vector3>` | `assignedAppsLogos` | Logo positions |
| `Il2CppReferenceArray<Sprite>` | `appTypesLogos` | Sprite array for app type icons |

### Instance Properties / Fields — Prefabs / Scene References
| Type | Name | Notes |
|------|------|-------|
| `Il2CppReferenceArray<GameObject>` | `serverPrefabs` | Server prefabs by type |
| `Il2CppReferenceArray<GameObject>` | `switchesPrefabs` | Switch prefabs |
| `Il2CppReferenceArray<GameObject>` | `patchPanelsPrefabs` | Patch panel prefabs |
| `GameObject` | `rackPackedPrefab` | Packed rack prefab |
| `GameObject` | `rackPrefab` | Standard rack prefab |
| `GameObject` | `rackMounts` | Rack mount points |
| `Il2CppReferenceArray<GameObject>` | `cableSpinnerPrefab` | Cable spinner prefabs |
| `Il2CppReferenceArray<GameObject>` | `sfpPrefabs` | SFP module prefabs |
| `Il2CppReferenceArray<GameObject>` | `sfpsBoxedPrefab` | Boxed SFP prefabs |
| `GameObject` | `emptySfpBox` | Empty SFP box |
| `GameObject` | `walls` | Walls parent object |
| `GameObject` | `particleSystemBrokenDevice` | Broken-device particle effect |
| `Transform` | `parentUsableObjects` | Parent for all usable objects |
| `Transform` | `placeToRespawnLostUsableObjects` | Lost-and-found respawn point |
| `int` | `lastUsedRackPositionGlobalUID` | UID counter for rack positions |
| `Il2CppReferenceArray<GameObject>` | `techniciansPrefabs` | Technician prefabs |
| `GameObject` | `prefabPushTrolleySupporterPack` | Trolley prefab |
| `Transform` | `defaultTrolleyPosition` | Default trolley position |

### Methods — Customer / Subnet Management
| Signature | Notes |
|-----------|-------|
| `CustomerItem GetCustomerItemByID(int customerID)` | Look up a customer by ID |
| `void ShuffleAvailableCustomers()` | Randomize customer selection |
| `void ShuffleAvailableSubnets()` | Randomize subnet pool |
| `string GetFreeSubnet(float appRequirements)` | Get a suitable free subnet |
| `bool IsSubnetValid(string subnet)` | Validate a subnet string |
| `void RemoveUsedSubnet(string subnet)` | Mark a subnet as used |
| `void ReturnSubnet(string subnet)` | Return a subnet to the free pool |
| `float GetCustomerTotalRequirement(CustomerItem customer)` | Get total speed requirement |
| `bool IsCustomerSuitableForBase(CustomerItem customer, int customerBaseID)` | Check compatibility |

### Methods — UI / Canvas
| Signature | Notes |
|-----------|-------|
| `void ShowCustomerCardsCanvas(CustomerBaseDoor door)` | Open customer choice UI |
| `void ButtonCustomerChosen(int cardID)` | Simulate choosing a customer card |
| `void ButtonCancelCustomerChoice()` | Cancel customer choice |
| `void ShowBuyWallCanvas(Wall wall)` | Open wall purchase UI |
| `void ButtonBuyWall()` | Confirm wall purchase |
| `void ButtonCancelBuyWall()` | Cancel wall purchase |
| `void ShowNetworkConfigCanvas(NetworkSwitch ns)` | Open switch config UI |
| `void CloseNetworkConfigCanvas()` | Close switch config UI |
| `void OpenAnyCanvas()` | Generic canvas open handler |
| `void CloseAnyCanvas(bool isCustomerChoice = false)` | Generic canvas close handler |

### Methods — Save / Load / Misc
| Signature | Notes |
|-----------|-------|
| `void OnLoad()` | Called after loading a save |
| `IEnumerator AutoSaveCoroutine()` | Auto-save coroutine |
| `void SetAutoSaveInterval(float minutes)` | Change auto-save interval |
| `void SetAutoSaveEnabled(bool enabled)` | Enable/disable auto-save |
| `void RestartAutoSave()` | Restart the auto-save coroutine |
| `void ResetTrolleyPosition()` | Reset the trolley to default position |
| `void LoadTrolleyPosition(Vector3 pos, Quaternion rot)` | Set trolley transform |
| `string ReturnServerNameFromType(int type)` | Map server type int → display name |
| `string ReturnSwitchNameFromType(int type)` | Map switch type int → display name |

---

## 5. `PlayerManager` (extends `MonoBehaviour`)

Singleton player controller — movement, interaction, object-in-hand state.

### Enum: `PlayerManager.ObjectInHand`
| Value |
|-------|
| `None` |
| `Server1U` / `Server2U` / `Server3U` |
| `Switch` |
| `Rack` |
| `CableSpinner` |
| `PatchPanel` |
| `SFPModule` / `SFPBox` |

### Delegate: `PlayerManager.OnBuyingWall`
Multicast delegate (no parameters, void return). Fired on wall purchase.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `PlayerManager` | `instance` | Singleton accessor |

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `FirstPersonController` | `fpc` | FPS controller reference |
| `GameObject` | `playerGO` | Player game object |
| `bool` | `enabledMouseMovement` | Mouse look enabled |
| `bool` | `enabledPlayerMovement` | WASD movement enabled |
| `CinemachineCamera` | `vcam` | Virtual camera |
| `Image` | `imageWaitForAction` | Action progress image |
| `Transform` | `moveItemPosition` | Position in front of player |
| `bool` | `enabledRayLookInteract` | Raycast interaction enabled |
| `bool` | `isGamePaused` | Local pause flag |
| `bool` | `playerIsSitting` | Player is seated |
| `ObjectInHand` | `objectInHand` | What the player is carrying |
| `GameObject` | `objectInHandPositionGO` | The carried object |
| `int` | `numberOfObjectsInHand` | Count (for stacks like SFP boxes) |
| `GameObject` | `defaultActionDustParticle` | Dust effect for actions |

### Methods
| Signature | Notes |
|-----------|-------|
| `void ConfinedCursorforUI()` | Switch cursor to confined mode (for UI) |
| `void PlayerStopMovement()` | Freeze player movement |
| `void LockedCursorForPlayerMovement()` | Lock cursor for FPS movement |
| `void GainIOPSEffect()` | Play the XP/IOPS gain particle effect |

---

## 6. `NetworkMap` (extends `MonoBehaviour`)

Network topology graph — tracks devices, connections, LACP groups, cable routing.

### Instance Properties / Fields — Dictionaries (the core data)
| Type | Name | Notes |
|------|------|-------|
| `Dictionary<string, Device>` | `devices` | All registered devices by name |
| `Dictionary<int, CustomerBase>` | `customerBases` | Customer bases by ID |
| `Dictionary<string, Server>` | `servers` | Servers by name/ID |
| `Dictionary<string, NetworkSwitch>` | `switches` | Switches by name/ID |
| `Dictionary<string, Server>` | `brokenServers` | Broken servers |
| `Dictionary<string, NetworkSwitch>` | `brokenSwitches` | Broken switches |
| `Dictionary<int, LACPGroup>` | `lacpGroups` | LACP groups by ID |
| `int` | `nextLACPGroupId` | Next LACP group ID to assign |
| `Dictionary<string, HashSet<string>>` | `switchConnections` | Switch-to-device connections |
| `Dictionary<int, ValueTuple<string,string>>` | `cableConnections` | Cable ID → (endpoint A, endpoint B) |
| `Dictionary<string, List<string>>` | `adjacencyList` | Graph adjacency list |

### Methods — Device Management
| Signature | Notes |
|-----------|-------|
| `void ClearMap()` | Wipe the entire network map |
| `void RegisterCustomerBase(CustomerBase cb)` | Register a customer base |
| `void RegisterServer(Server server)` | Register a server |
| `void RegisterSwitch(NetworkSwitch ns)` | Register a switch |
| `void AddDevice(string name, TypeOfLink type, int customerID = -1)` | Add a generic device |
| `void RemoveDevice(string name)` | Remove a device |
| `Device GetDevice(string name)` | Look up a device by name |
| `List<Device> GetAllDevices()` | Get all devices |
| `string GenerateDeviceName(TypeOfLink type, Vector3 pos)` | Auto-generate a device name |

### Methods — Connections / Routing
| Signature | Notes |
|-----------|-------|
| `void Connect(string from, string to)` | Create a connection |
| `void Disconnect(string from, string to)` | Remove a connection |
| `List<List<string>> FindAllRoutes(string baseName, string serverName)` | Find all routes between a base and server |
| `List<List<string>> FindPhysicalPath(string start, string target)` | Find physical cable paths |
| `void RegisterCableConnection(int cableId, ...)` | Register a physical cable (many params) |
| `void AddSwitchConnection(string switchName, string deviceName)` | Add a switch↔device link |
| `void RemoveCableConnection(int cableId)` | Remove a cable connection |
| `void RemoveIsolatedDevices()` | Clean up unconnected devices |
| `string PrintNetworkMap()` | Debug dump of the entire map |
| `bool IsIpAddressDuplicate(string ip, Server exclude)` | Check for IP conflicts |
| `bool IsPatchPanelPort(string deviceName)` | Check if a device is a patch panel port |
| `string ResolveThroughPatchPanel(string port, string from)` | Resolve a patch panel connection |

### Methods — Customer / Speed Updates
| Signature | Notes |
|-----------|-------|
| `void UpdateCustomerServerCountAndSpeed(int custId, int count, float speed)` | Update customer metrics |
| `void UpdateDeviceCustomerID(string deviceName, int customerID)` | Reassign a device's customer |

### Methods — Broken Devices
| Signature | Notes |
|-----------|-------|
| `void AddBrokenServer(Server server)` | Mark server as broken |
| `void AddBrokenSwitch(NetworkSwitch ns)` | Mark switch as broken |
| `void RemoveBrokenServer(string serverId)` | Unmark server |
| `void RemoveBrokenSwitch(string switchId)` | Unmark switch |

### Methods — LACP
| Signature | Notes |
|-----------|-------|
| `int CreateLACPGroup(string deviceA, string deviceB, List<int> cableIds)` | Create a link aggregation group |
| `void RemoveLACPGroup(int groupId)` | Remove a LACP group |
| `void RemoveCableFromLACPGroups(int cableId)` | Remove a cable from its LACP groups |
| `void SetLACPGroups(Dictionary<int, LACPGroup> groups)` | Bulk-set LACP groups |

### Nested: `NetworkMap.Device`
| Type | Name | Notes |
|------|------|-------|
| `string` | `Name` | Device name |
| `CableLink.TypeOfLink` | `Type` | Link type (Ethernet, fiber, etc.) |
| `int` | `CustomerID` | Owning customer (-1 = none) |
| `HashSet<Device>` | `Connections` | Adjacent devices |

### Nested: `NetworkMap.LACPGroup`
| Type | Name | Notes |
|------|------|-------|
| `int` | `groupId` | |
| `string` | `deviceA` / `deviceB` | Endpoint device names |
| `List<int>` | `cableIds` | Cables in this LAG |
| `float GetAggregatedSpeed(Dictionary<int, CableInfo> cables)` | Total LAG bandwidth |

---

## 7. `Server` (extends `UsableObject`)

Individual server — processing, IP, customer assignment, health, power state.

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `string` | `ServerID` | Unique server identifier |
| `int` | `serverType` | Type index (1U/2U/3U) |
| `string` | `IP` | Assigned IP address |
| `int` | `appID` | Assigned application ID |
| `float` | `maxProcessingSpeed` | Maximum IOPS/speed |
| `float` | `currentProcessingSpeed` | Current speed |
| `float` | `previousProcessingSpeed` | Previous tick's speed |
| `bool` | `isOn` | Power state |
| `bool` | `isBroken` | Is this server broken |
| `int` | `timeToBrake` | Ticks until breakdown (countdown) |
| `int` | `eolTime` | End-of-life time |
| `int` | `existingWarningSigns` | Active warning sign count |
| `int` | `existingErrorSigns` | Active error sign count |
| `bool` | `isWarningCleared` | Has the warning been acknowledged |
| `bool` | `hasInitialized` | Has the server finished init |
| `List<CableLink>` | `activeLinks` | Currently active cable connections |
| `Il2CppReferenceArray<CableLink>` | `cablelinks` | All cable link slots |
| `Image` | `customerLogo` | Customer logo on the server screen |
| `Image` | `appLogo` | App logo on the server screen |
| `GameObject` | `canvas` | Server screen UI canvas |
| `TextMeshProUGUI` | `txtIP` | IP text on the screen |
| `TextMeshProUGUI` | `txtServerScreen` | Screen text |
| `Coroutine` | `breakingRoutine` | Active breaking coroutine |

### Methods
| Signature | Notes |
|-----------|-------|
| `void PowerButton(bool forceState = false)` | Toggle power (or force on/off) |
| `bool IsAnyCableConnected()` | Check if any cable is connected |
| `void ServerInsertedInRack(ServerSaveData data = null)` | Called when placed into a rack |
| `void RegisterLink(CableLink link)` | Register a new cable link |
| `void UnregisterLink(CableLink link)` | Unregister a cable link |
| `void UpdateServerScreenUI()` | Refresh the screen display |
| `void ButtonClickChangeCustomer(bool forward)` | Cycle customer assignment |
| `int GetNextCustomerID(int currentID, bool forward)` | Get next valid customer ID |
| `void ButtonClickChangeIP()` | Open IP change dialog |
| `void SetIP(string ip)` | Set the server's IP address |
| `int GetCustomerID()` | Get the assigned customer ID |
| `void UpdateCustomer(int newCustomerID)` | Change customer assignment |
| `void UpdateAppID(int appID)` | Change app assignment |
| `bool ValidateRackPosition()` | Check if correctly placed in rack |
| `void ClearWarningSign(bool isPreserved = false)` | Clear a warning sign |
| `void ClearErrorSign()` | Clear an error sign |
| `void RepairDevice()` | Repair this server |
| `void SetPowerLightMaterial(Material mat)` | Change the power LED material |

---

## 8. `NetworkSwitch` (extends `UsableObject`)

Network switch device — similar lifecycle to `Server`.

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `string` | `switchId` | Unique ID |
| `int` | `switchType` | Type index |
| `bool` | `isOn` | Power state |
| `string` | `label` | Display label |
| `int` | `timeToBrake` | Countdown to breakdown |
| `int` | `eolTime` | End-of-life time |
| `bool` | `isBroken` | Broken state |
| `int` | `existingWarningSigns` / `existingErrorSigns` | Sign counters |
| `bool` | `isWarningCleared` | Warning acknowledged |
| `Coroutine` | `breakingRoutine` | Breaking coroutine |
| `GameObject` | `canvas` | Screen canvas |
| `TextMeshProUGUI` | `txtScreen` | Screen text |

### Methods
| Signature | Notes |
|-----------|-------|
| `void PowerButton(bool forceState = false)` | Toggle power |
| `bool IsAnyCableConnected()` | Check cable connections |
| `void SwitchInsertedInRack(SwitchSaveData data = null)` | Placed in rack |
| `void DisconnectCablesWhenSwitchIsOff()` | Disconnect all cables on power-off |
| `void HandleNewCableWhileOff(int cableId)` | Handle cable plugged in while off |
| `string GetSwitchId()` | Get switch ID |
| `void UpdateScreenUI()` | Refresh screen |
| `void DisconnectCables()` | Disconnect all |
| `void ReconnectCables()` | Reconnect all |
| `bool ValidateRackPosition()` | Validate placement |
| `void ButtonShowNetworkSwitchConfig()` | Open config UI |
| `void ClearWarningSign(bool isPreserved = false)` | Clear warning |
| `void ClearErrorSign()` | Clear error |

---

## 9. `StaticUIElements` (extends `MonoBehaviour`) — HUD / Notifications

The main HUD controller. Manages top-left stats, notifications, messages, cursor hints.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `int` | `MAX_MESSAGES` | Max messages in the message field |
| `float` | `MESSAGE_DURATION` | How long messages stay visible |

### Instance Properties / Fields — HUD Elements
| Type | Name | Notes |
|------|------|-------|
| `GameObject` | `canvasStatic` | The main static HUD canvas |
| `GameObject` | `imagePointer` | Pointer/crosshair image |
| `TextMeshProUGUI` | `txtUnderPointer` | Tooltip text under cursor |
| `Image` | `holdKeyIndicator` | Hold-key progress ring |
| `Image` | `nextToPointer` | Sprite shown next to cursor |
| `Image` | `blackOver` | Black overlay (fade to black) |
| `GameObject` | `loading` | Loading screen GO |
| `TextMeshProUGUI` | `txtMessagesField` | Message log text |
| `GameObject` | `errorSignPrefab` | Prefab for error signs |
| `GameObject` | `warningSignPrefab` | Prefab for warning signs |
| `int` | `nextErrorWarningUID` | UID counter |

### Instance Properties / Fields — Top-Left Stats
| Type | Name | Notes |
|------|------|-------|
| `GameObject` | `topLeft_coinsPrestige` | Stats container |
| `TextMeshProUGUI` | `topLeft_coinTXT` | Money display |
| `TextMeshProUGUI` | `topLeft_MoneyPerSecond` | Money/sec rate |
| `TextMeshProUGUI` | `topLeft_ExpensesPerSecond` | Expenses/sec rate |
| `TextMeshProUGUI` | `topLeft_reputationTXT` | Reputation display |
| `TextMeshProUGUI` | `topLeft_xpTXT` | XP display |
| `TextMeshProUGUI` | `topLeft_XPPerSecond` | XP/sec rate |

### Instance Properties / Fields — Notification Banner
| Type | Name | Notes |
|------|------|-------|
| `GameObject` | `notificationGO` | Notification banner game object |
| `Image` | `notificationSprite` | Notification icon |
| `TextMeshProUGUI` | `notificationText` | Notification text |

### Instance Properties / Fields — Input / Key Hints
| Type | Name | Notes |
|------|------|-------|
| `bool` | `isKeyboardOrMouse` | Input device detection |
| `GameObject` | `keyhint_buttonE` | "Press E" hint |
| `Transform` | `keyboardParent` | Parent for key hint prefabs |
| `GameObject` | `keyHintCustomPrefab` | Custom key hint prefab |
| `GameObject` | `prefabParticleUpgrade` | Upgrade particle effect |
| `TextMeshProUGUI` | `txtLoadingInfo` | Loading screen info text |

### Methods
| Signature | Notes |
|-----------|-------|
| `void CalculateRates(out float moneyPerSec, out float xpPerSec, out float expensesPerSec)` | **Very useful:** compute live financial rates |
| `void SetNotification(int localisationUID, Sprite sprite = null, string text = "")` | **Show a notification banner** |
| `void ShowStaticCanvas(bool active)` | Show/hide the HUD |
| `GameObject CreateCustomKeyHint(InputAction action, int textUID, Transform parent = null, bool isPermanent = false)` | Create a key hint |
| `void RemoveCustomKeyHint()` | Remove custom key hint |
| `void InstantiateParticleUpgrade(Transform t)` | Spawn upgrade particles |
| `void UpdateMessageDisplay()` | Refresh message field |
| `void AddMeesageInField(string message)` | **Add a message to the message log** (note: typo is in the game) |
| `int InstantiateErrorWarningSign(bool isError, Vector3 objectPos)` | Spawn an error/warning sign at position |
| `void DestroyErrorWarningSign(int uid)` | Destroy a sign by UID |
| `void ShowSpriteNextToPointer(Sprite s)` | Show a sprite next to cursor |
| `void ClearSpriteNextToPointer()` | Clear cursor sprite |
| `void ShowTextUnderCursor(string text)` | Show tooltip text |
| `void HideTextUnderCursor()` | Hide tooltip text |
| `void UpdateHoldProgress(float value)` | Update the hold-key progress (0..1) |
| `void SetLoadingInfo(string s)` | Set loading screen text |

---

## 10. `TimeController` (extends `MonoBehaviour`)

Singleton day/night cycle and time tracking.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `TimeController` | `instance` | Singleton accessor |
| `OnEndOfTheDay` | `onEndOfTheDayCallback` | Event: end of day |

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `float` | `secondsInFullDay` | Real seconds per game day |
| `float` | `currentTimeOfDay` | Current time (0.0–1.0 normalized) |
| `float` | `timeMultiplier` | Speed multiplier |
| `int` | `day` | Current day number |

### Methods
| Signature | Notes |
|-----------|-------|
| `bool TimeIsBetween(float startHour, float endHour)` | Check if current time is in range |
| `float CurrentTimeInHours()` | Get current time as hours (0–24) |
| `int HoursFromDate(float time, int day)` | Convert to total hours |

---

## 11. `CustomerBase` (extends `MonoBehaviour`)

A customer tenant in the data center — tracks SLA, app performance, revenue.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `float` | `EPSILON` | Floating-point comparison epsilon |
| `int` | `satisfiedCustomerCount` | Global count of satisfied customers |

### Instance Properties / Fields
| Type | Name | Notes |
|------|------|-------|
| `int` | `customerBaseID` | Base slot ID |
| `int` | `customerID` | Customer data ID |
| `Image` | `customerLogo` | Logo image reference |
| `CustomerItem` | `customerItem` | Customer definition |
| `TextMeshProUGUI` | `txtNumberOfServers` | Server count display |
| `TextMeshProUGUI` | `txtCurrentSpeed` | Current speed display |
| `float` | `currentSpeed` | Current total processing speed |
| `int` | `howLongToWaitBeforeFine` | Grace period before penalties |
| `float` | `currentTotalAppSpeeRequirements` | Current total requirements |
| `float` | `maximumAppRequirementsSpeedTotal` | Max possible requirement |
| `bool` | `wasFullySatisfied` | Was fully satisfied last check |

### Methods
| Signature | Notes |
|-----------|-------|
| `IEnumerator CheckIfAppRequirementsAreMet()` | Continuous SLA check |
| `IEnumerator UpdateMoney()` | Continuous revenue/penalty generation |
| `bool AreAllAppRequirementsMet()` | One-shot SLA check |
| `void UpdateCustomerServerCountAndSpeed(int count, float speed)` | Update metrics |
| `void AddAppPerformance(int appID, float speed)` | Add speed to an app |
| `void ResetAllAppSpeeds()` | Zero out all app speeds |
| `bool IsIPPresent(string ip)` | Check if an IP is configured here |
| `int GetAppIDForIP(string ip)` | Get app ID for an IP |
| `void SetUpBase(CustomerItem item, CustomerBaseSaveData data = null)` | Initialize with a customer |
| `void SetUpApp(int appID, int difficulty, CustomerBaseSaveData data = null)` | Set up an app slot |
| `string AppText(int appID, string subnet)` | Generate display text for an app |
| `void UpdateSpeedOnCustomerBaseApp(int appID, float speed)` | Update per-app speed |
| `int GetServerTypeForIP(string ip)` | Get required server type for IP |
| `float GetTotalAppSpeed()` | Get total app speed |
| `void LoadData(CustomerBaseSaveData data)` | Restore from save |

---

## 12. `CustomerItem` (extends `ScriptableObject`)

Static definition of a customer — what they need, what they pay.

### Fields
| Type | Name | Notes |
|------|------|-------|
| `int` | `customerID` | Unique ID |
| `string` | `customerName` | Display name |
| `Sprite` | `logo` | Company logo sprite |
| `Il2CppStructArray<int>` | `appTypes` | App type IDs this customer needs |
| `int` | `difficulty` | Difficulty tier |
| `int` | `reputation` | Reputation reward/requirement |
| `float` | `sla` | SLA target (speed threshold) |
| `int` | `downtimeFinePerHour` | Penalty per hour of downtime |

---

## 13. `SaveSystem` (static utility class)

Save/load system.

### Static Fields
| Type | Name | Notes |
|------|------|-------|
| `string` | `saveDirPath` | Save directory path |
| `string` | `loadSaveName` | Name of save being loaded |
| `int` | `version` | Current save version |
| `int` | `versionToIgnoreFrom` | Compat cutoff version |
| `bool` | `isQuitting` | App is quitting flag |

### Static Methods
| Signature | Notes |
|-----------|-------|
| `void SaveGame(string savename = null, string stringNameOfSave = null)` | Save game |
| `SaveData LoadGame(string savename)` | Load and return save data |
| `void DeleteSaveFile(string savename)` | Delete a save |
| `void SaveGameData()` | Serialize current game state |
| `string NewestSave()` | Get the newest save file name |
| `void LoadGameData()` | Apply loaded data to game |
| `void Load(string savename, bool isFromPauseMenu)` | Full load pipeline |
| `void AutoSave()` | Trigger auto-save |

---

## Singleton Access Pattern (from Rust via Il2Cpp FFI)

All major managers follow the singleton pattern:

```
MainGameManager.instance
PlayerManager.instance
BalanceSheet.instance
TechnicianManager.instance
TimeController.instance
```

These are static fields returning the live MonoBehaviour instance. From Rust FFI, you would:

1. Resolve the Il2Cpp class pointer for e.g. `Il2Cpp.MainGameManager`
2. Read the static field `instance`
3. Use the instance pointer to call methods or read fields

---

## Key Modding Use Cases

| Use Case | Class(es) | Key Members |
|----------|-----------|-------------|
| Read player money/XP/rep | `StaticUIElements` | `CalculateRates()`, `topLeft_coinTXT`, etc. |
| Show notifications | `StaticUIElements` | `SetNotification()`, `AddMeesageInField()` |
| Financial data | `BalanceSheet` | `currentRecords`, `history`, `GetLatestSnapshot()` |
| Server health monitoring | `Server`, `NetworkMap` | `isBroken`, `brokenServers`, `brokenSwitches` |
| Network topology | `NetworkMap` | `devices`, `FindAllRoutes()`, `PrintNetworkMap()` |
| Time/day info | `TimeController` | `day`, `CurrentTimeInHours()`, `timeMultiplier` |
| Technician status | `Technician`, `TechnicianManager` | `isBusy`, `currentState`, `QueuedJobCount` |
| Customer SLA status | `CustomerBase` | `AreAllAppRequirementsMet()`, `currentSpeed` |
| Save/load | `SaveSystem` | `SaveGame()`, `LoadGame()`, `AutoSave()` |
| Pause/unpause | `MainGameManager` | `isGamePaused` |
| Player interaction lock | `PlayerManager` | `enabledRayLookInteract`, `PlayerStopMovement()` |
| Spawn error/warning signs | `StaticUIElements` | `InstantiateErrorWarningSign()` |