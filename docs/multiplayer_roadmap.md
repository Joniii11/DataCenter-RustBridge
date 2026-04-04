# Data Center Multiplayer Mod — Roadmap & Design Document

## Status: PHASE 1 — IN PROGRESS

Tracking document for adding Co-op Multiplayer to Data Center via the modloader.
Reference this file in any thread to continue development.

## Design Decisions (Locked)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | Multiplayer Type | **Co-op Shared World** | Players share one data center, build and manage together (like Satisfactory/Valheim) |
| 2 | Network Architecture | **Host-Client (Listen Server)** | Host's game is the "source of truth". Singleton managers run normally on host. No dedicated server needed. Save lives on host. |
| 3 | Player Count | **2–4 players, MVP with 2** | Sweet spot for tycoon co-op. Steam P2P scales fine to 4. |
| 4 | Permissions | **Everyone can do everything** | All players are equal. Shared money account. Only host can save/load and control game speed. |
| 5 | Transport Layer | **Steam Networking Sockets** | Already in-game via Steamworks.NET. NAT traversal, encryption, relay servers — all free. No port forwarding. Steam invites work out of the box. |
| 6 | Code Split | **Hybrid: Rust-heavy, thin C#** | User knows Rust, not C#. Complex logic (protocol, sync, lobby) lives in Rust. C# is a thin relay: Harmony patches → FFI → Rust, and Rust → FFI → Unity actions. |
| 7 | Sync Strategy | **Hybrid: Actions + State** | Discrete events (place server, pull cable) via reliable Action messages. Continuous data (player position, money, time) via periodic State sync. |
| 8 | Mod Compatibility | **Host-mods required, client extras allowed** | On join: client must have all host mods (name + version). Extra client-only mods are OK. Mismatch → join rejected with "missing mods" message. |
| 9 | Remote Player Model | **Technician prefab (MVP)** | Game already has animated 3D technician models (`techniciansPrefabs`). Clone one, add nametag, sync position. Upgrade to UMA avatars later. |

## Existing Architecture (What We Have)

```
Game (Data Center — Unity 6, IL2CPP, SINGLEPLAYER)
  └── MelonLoader v0.7.2
        └── RustBridge.dll (C# MelonMod)
              ├── Core.cs             — Entry point, loads Rust mods
              ├── FFIBridge.cs        — Loads native DLLs from Mods/native/
              ├── GameAPI.cs          — Function pointer table (v5, append-only)
              ├── GameHooks.cs        — Safe Il2Cpp accessors to game state
              ├── HarmonyPatches.cs   — Patches that fire events
              ├── EventSystem.cs      — 30 events across 9 categories
              ├── CustomEmployeeManager.cs — HR panel injection for custom employees
              └── [NEW] MultiplayerBridge.cs — Thin networking relay (to be built)
                    │
                    └── Rust mods via FFI (Mods/native/)
                          ├── dc_api          — Shared FFI types + safe wrappers
                          ├── dc_netwatch     — SysAdmin auto-repair mod
                          ├── dc_example_mod  — Example mod
                          └── [NEW] dc_multiplayer — Multiplayer core (to be built)
```

### Key Game Singletons (all accessible via `get_Instance`)

| Singleton | What it controls |
|-----------|-----------------|
| `MainGameManager` | Central orchestrator, all prefabs, UID counters |
| `PlayerManager` | Player position, movement, object-in-hand, camera |
| `NetworkMap` | All servers, switches, cables, broken/EOL device lists |
| `BalanceSheet` | Money, revenue, expenses, salary tracking |
| `TechnicianManager` | Technician hiring, dispatch, job queue |
| `TimeController` | Game time, day counter, day length, speed |
| `SaveSystem` | Save/Load (static class) |

### Key Game Facts

- **Steamworks.NET** already integrated (leaderboards/stats only, NO networking)
- **NO existing multiplayer** — no lobby, no P2P, no netcode whatsoever
- Game uses **MonoBehaviour + Singletons** (not ECS) — easy to hook
- Dev added official modding in v1.0.40 (OBJ files in StreamingAssets/Mods for shop items)
- All game actions go through singleton managers → single place to intercept

### Current FFI (API v5, 40+ function pointers)

Covers: money/xp/rep, time, device counts, broken/EOL queries, technician dispatch,
custom employees (HR), notifications, financial rates, pause, save, difficulty.

Event system: 30 events (economy, server, time, customer, shop, employee, persistence, building, mod).

## Multiplayer Architecture (Target)

```
HOST MACHINE                              CLIENT MACHINE
┌─────────────────────────┐               ┌─────────────────────────┐
│ Data Center (normal)    │               │ Data Center (modded)    │
│                         │               │                         │
│ C# MultiplayerBridge    │               │ C# MultiplayerBridge    │
│  ├─ Harmony patches     │               │  ├─ Action executor     │
│  │  (intercept actions) │               │  │  (apply remote cmds) │
│  ├─ State reader        │               │  ├─ Player renderer     │
│  │  (money,time,pos)    │               │  │  (spawn/move models) │
│  └─ FFI ←→ Rust         │               │  └─ FFI ←→ Rust         │
│         │                │               │         │                │
│ Rust: dc_multiplayer    │               │ Rust: dc_multiplayer    │
│  ├─ Host logic          │  Steam P2P    │  ├─ Client logic        │
│  │  ├─ Lobby mgmt      │◄────────────►│  │  ├─ Lobby join       │
│  │  ├─ Action handler   │  (encrypted,  │  │  ├─ Action sender    │
│  │  ├─ State broadcast  │   relayed)    │  │  ├─ State receiver   │
│  │  └─ Mod registry     │               │  │  └─ Mod registry     │
│  ├─ Protocol (serde)    │               │  ├─ Protocol (serde)    │
│  └─ Steam Networking    │               │  └─ Steam Networking    │
│     (steamworks crate)  │               │     (steamworks crate)  │
└─────────────────────────┘               └─────────────────────────┘
```

### Message Flow: Client Wants to Place a Server

```
CLIENT                          HOST                           ALL CLIENTS
  │                               │                               │
  │  1. Player presses "place"    │                               │
  │  2. C# patch intercepts       │                               │
  │  3. BLOCKS local execution    │                               │
  │  4. Sends ActionRequest       │                               │
  │     via FFI → Rust            │                               │
  │  ──── PlaceServer{rack,slot} ──►                              │
  │                               │  5. Host validates request    │
  │                               │  6. Host executes action      │
  │                               │     (calls real game API)     │
  │                               │  7. Host broadcasts result    │
  │                               │  ──── ServerPlaced{id,pos} ──►│
  │  ◄── ServerPlaced{id,pos} ────│                               │
  │  8. Client spawns server      │                               │
  │     visual (or confirms)      │                     9. Others │
  │                               │                     update too│
```

### Sync Detail

**Action Sync (reliable, ordered)**
Sent when a player performs a discrete game action.
Host executes, broadcasts result.

| Action | Key Data |
|--------|----------|
| PlaceRack | position, rotation |
| PlaceServer | rack_id, slot, server_type |
| PullCable | endpoint_a, endpoint_b, cable_type |
| ConfigureSwitch | switch_id, ip, subnet, vlan, lacp_config |
| AcceptCustomer | customer_id, slot_id |
| HireTechnician | — |
| FireTechnician | technician_id |
| HireCustomEmployee | employee_id (mod registry) |
| FireCustomEmployee | employee_id |
| ShopBuy | item_list, total_cost |
| PurchaseWall | wall_id, position |

**State Sync (unreliable, frequent)**
Host pushes periodically, clients interpolate.

| Data | Frequency | Transport |
|------|-----------|-----------|
| Player positions + rotation | ~20 Hz (50ms) | Unreliable |
| Player animation state | ~10 Hz | Unreliable |
| Money / XP / Reputation | 1 Hz | Unreliable |
| Time of day / Day counter | 1 Hz | Unreliable |
| Game speed | On change | Reliable |
| Technician positions | ~5 Hz (if visible) | Unreliable |

## Mod Compatibility Protocol

```
HOST creates lobby:
  → Sets lobby metadata:
    "mp_version" = "1"
    "mod_count"  = "2"
    "mod_0"      = "dc_netwatch:1.1.0:a3f2b1c8"
    "mod_1"      = "dc_example_mod:0.1.0:ff12c8d2"

CLIENT wants to join:
  → Reads lobby metadata
  → Compares with locally loaded mods
  → For each host mod: check name + version match
  → Result:
    ✅ All host mods present             → JOIN
    ⚠️ Client has extra mods             → JOIN (client-only mods OK)
    ❌ Missing host mod or version wrong  → REJECT + show missing list
```

### New FFI Functions Needed

```
register_mod_info(name, version, hash) → already implicit via mod_info()
get_loaded_mods() → returns list of (name, version, hash) for all loaded mods
```

## Implementation Phases

### Phase 1 — "Ghost Friend" (MVP) 👻
> Two players see each other walking around. Nothing else synced.

**Goal**: Prove networking works end-to-end through the full stack.

Split into **Phase 1a** (scaffolding, no lobby) and **Phase 1b** (lobby + invites).

#### Phase 1a — Direct P2P Connection (SCAFFOLDING DONE ✅)

**Key Decision**: Instead of using the `steamworks` Rust crate (risk: conflicts with game's
already-initialized Steam), we expose Steam functions from C# via the existing GameAPI
function pointer table. C# calls `steam_api64.dll` directly via DllImport. Rust gets
everything through the proven FFI pattern.

**New Crate: `dc_multiplayer`** (Rust) ✅
- [x] Project scaffolding (Cargo.toml, workspace member)
- [x] Binary protocol with serde + bincode (`protocol.rs` — Message enum: Position, Hello, Welcome, Goodbye, Ping/Pong)
- [x] Remote player tracking (`player.rs` — PlayerTracker, RemotePlayerData FFI struct)
- [x] Networking layer wrapping Steam P2P via GameAPI (`net.rs` — send_message, read_all_messages, accept_session)
- [x] Mod entry point with 20Hz position sync (`lib.rs`)
- [x] Host/client role detection
- [x] Disconnect handling (Goodbye message, stale player cleanup after 10s)
- [x] FFI exports for C#: `mp_get_remote_players()`, `mp_is_connected()`, `mp_is_host()`, `mp_get_player_count()`, `mp_host()`, `mp_connect(steam_id)`, `mp_disconnect()`, `mp_get_my_steam_id()`
- [x] Handshake protocol (Hello → Welcome with player name + mod version)
- [x] Cargo build passes ✅

**New FFI: GameAPI v7 — Steam / Multiplayer** ✅
- [x] 17 new function pointers appended to GameAPI struct (dc_api + C# GameAPITable)
- [x] Steam identity: `steam_get_my_id`, `steam_get_friend_name`
- [x] Lobby stubs (Phase 1b): `steam_create_lobby`, `steam_join_lobby`, `steam_leave_lobby`, `steam_get_lobby_id`, `steam_get_lobby_owner`, `steam_get_lobby_member_count`, `steam_get_lobby_member_by_index`, `steam_set_lobby_data`, `steam_get_lobby_data`
- [x] P2P networking (working): `steam_send_p2p`, `steam_is_p2p_available`, `steam_read_p2p`, `steam_accept_p2p`
- [x] Event polling stub: `steam_poll_event`
- [x] Player position: `get_player_position` (reads from PlayerManager)
- [x] Safe Rust wrappers in `impl Api` with version check (`api_version >= 7`)
- [x] C# implementations via direct DllImport to `steam_api64.dll` (no Steamworks.NET callbacks needed)
- [x] API_VERSION bumped to 7 in both C# and Rust

**New C# File: `MultiplayerBridge.cs`** ✅
- [x] Detects `dc_multiplayer.dll` via GetModuleHandle + resolves FFI exports
- [x] Polls `mp_get_remote_players()` every frame when connected
- [x] Spawns technician prefab clone for each remote player (strips NavMeshAgent + Technician AI)
- [x] Capsule fallback if technician prefab unavailable
- [x] Smooth position/rotation interpolation (Lerp @ 10x deltaTime)
- [x] World-space TextMeshPro nametag above player head
- [x] BillboardNameTag component (registered in Il2Cpp, always faces camera)
- [x] Destroys remote player GameObjects on disconnect / stale
- [x] Wired into Core.cs (OnUpdate, Shutdown)
- [x] dotnet build passes ✅

**Remaining for Phase 1a — Runtime Testing**:
- [ ] Install to game directory (tools/install.ps1)
- [ ] Verify Steam ID is retrieved correctly in-game
- [ ] Test P2P connection between two Steam accounts
- [ ] Verify AcceptP2PSessionWithUser works without callback (pre-accept approach)
- [ ] Confirm remote player model appears and moves
- [ ] Verify nametag rendering
- [ ] Test disconnect/reconnect flow
- [ ] Add UI for showing Steam ID + connect command (currently must be triggered externally)

#### Phase 1b — Steam Lobby + Friend Invites (TODO)

- [ ] Implement lobby C# stubs (SteamMatchmaking via DllImport)
- [ ] Lobby create/join/leave in Rust
- [ ] Steam friend invite system
- [ ] Lobby metadata for mod registry check
- [ ] Connection handshake validates mod lists
- [ ] In-game UI: Create Lobby / Join / Invite buttons

**Definition of Done**: Two Steam friends, both running the mod, can create/join a lobby and see each other's technician models walking around in the data center.

### Phase 2 — "Spectator+" 👁️
> Client sees the host's world changing in real-time.

**Goal**: One-way state sync. Client world reflects host's state.

**Rust (`dc_multiplayer`)**
- [ ] State snapshot system (money, xp, rep, time, day, speed)
- [ ] State broadcast from host (1 Hz reliable)
- [ ] World-object delta sync (new server placed, cable connected, etc.)
- [ ] Object ID mapping (host object UID → sync message)

**C# (`MultiplayerBridge.cs`)**
- [ ] Harmony patches on key game methods to detect changes:
  - `Server.GettingNewServer` / placement methods
  - `CableLink` connection methods
  - `Rack` mount/unmount
  - `NetworkSwitch` configuration changes
  - `BalanceSheet.UpdateMoney`
  - `TimeController` changes
  - `TechnicianManager.AddTechnician` / `FireTechnician`
  - `CustomerBase` accept/reject
  - `ShopCartItem` checkout
- [ ] Each patch: serialize event → FFI call → Rust broadcasts to clients
- [ ] State applier: receive state updates from Rust → apply to local game singletons
- [ ] Object spawner: receive "server placed at position X" → instantiate prefab

**New FFI Functions (API v7)**
- [ ] `mp_send_action(action_type: u32, data: *const u8, len: u32) -> i32`
- [ ] `mp_poll_actions(buf: *mut u8, max_len: u32) -> u32`
- [ ] `mp_send_state_snapshot(data: *const u8, len: u32)`
- [ ] `mp_poll_state(buf: *mut u8, max_len: u32) -> u32`

**Definition of Done**: Host plays normally. Client joins and sees: money updating, time passing, and when host places a server or pulls a cable, it appears on the client's screen.

### Phase 3 — "Real Co-op" 🤝
> Both players can interact with the world.

**Goal**: Client actions are validated by host and applied globally.

**Rust (`dc_multiplayer`)**
- [ ] Action request/response protocol
- [ ] Host-side action validator (does player have enough money? is rack slot free?)
- [ ] Action queue (prevent race conditions when 2 players act simultaneously)
- [ ] Action result broadcast (success → all clients apply, fail → requester gets error)
- [ ] Object-in-hand sync (client is carrying a rack, others see it)

**C# (`MultiplayerBridge.cs`)**
- [ ] Client-side action interceptor:
  - When client tries to place server → BLOCK local execution
  - Serialize action → FFI → Rust → send to host
  - Wait for response → apply or show error
- [ ] Host-side action executor:
  - Receive action from Rust → call real game methods
  - Result → FFI → Rust → broadcast
- [ ] Object-in-hand visual sync
- [ ] Interaction blocking (prevent two players interacting with same object)

**New FFI Functions (API v8)**
- [ ] `mp_request_action(action_type: u32, data: *const u8, len: u32) -> u32` (returns request_id)
- [ ] `mp_poll_action_results(buf: *mut u8, max_len: u32) -> u32`
- [ ] `mp_set_object_in_hand(object_type: u32, object_id: u32)`

**Definition of Done**: Two players can both walk around AND interact with the world. One player places a rack while the other pulls cables. Both see each other's changes. Shared money works.

### Phase 4 — "Polish" ✨
> Make it feel good.

- [ ] Remote player animations (walk, idle, interact, carry)
- [ ] UMA avatar system (unique player appearance)
- [ ] In-game multiplayer menu (create/join/invite UI)
- [ ] Player list overlay (who's connected, ping)
- [ ] Chat system (text)
- [ ] Voice chat (Steam Voice API)
- [ ] 3-4 player testing & bandwidth optimization
- [ ] Host migration (optional, complex)
- [ ] Permissions system (optional — lock certain actions per player)
- [ ] Pause consensus (if host pauses, all pause)
- [ ] Save notification (all players see "game saved")

## Project Structure (Current)

```
E:\rust\data center modloader\
├── csharp/DataCenterModLoader/
│   ├── Core.cs                      # Entry point (updated: wires MultiplayerBridge)
│   ├── FFIBridge.cs                 # Loads Rust DLLs
│   ├── GameAPI.cs                   # Function pointer table (v7, 17 new Steam/MP fields)
│   ├── GameHooks.cs                 # Safe Il2Cpp accessors
│   ├── HarmonyPatches.cs            # Event-firing patches
│   ├── EventSystem.cs               # 30 events across 9 categories
│   ├── CustomEmployeeManager.cs     # HR panel injection
│   └── MultiplayerBridge.cs         # [NEW] Remote player rendering, polls dc_multiplayer
│
├── crates/
│   ├── dc_api/                      # FFI types + safe wrappers (v7, Steam/MP wrappers added)
│   ├── dc_netwatch/                 # SysAdmin auto-repair mod (untouched)
│   ├── dc_example_mod/              # Example mod (untouched)
│   └── dc_multiplayer/              # [NEW] Core multiplayer crate
│       ├── src/
│       │   ├── lib.rs               # Mod entry, update loop, FFI exports (mp_*)
│       │   ├── protocol.rs          # Message enum (serde + bincode)
│       │   ├── player.rs            # RemotePlayerData FFI struct, PlayerTracker
│       │   └── net.rs               # Steam P2P send/receive via GameAPI wrappers
│       └── Cargo.toml               # deps: dc_api, serde, bincode
│
├── Cargo.toml                       # Workspace (dc_multiplayer added)
└── docs/
    └── multiplayer_roadmap.md       # This file
```

Future files (Phase 2+):
- `dc_multiplayer/src/lobby.rs` — Steam lobby management
- `dc_multiplayer/src/sync.rs` — State snapshot & delta sync
- `dc_multiplayer/src/host.rs` — Host-side action validation
- `dc_multiplayer/src/client.rs` — Client-side action requests
- `dc_multiplayer/src/registry.rs` — Mod compatibility checking

## Dependencies

### Rust (new for multiplayer)
| Crate | Purpose | Status |
|-------|---------|--------|
| `serde` (1.x, features=derive) | Serialization framework | ✅ Added |
| `bincode` (1.x) | Fast binary serialization for network messages | ✅ Added |

**NOT using `steamworks` crate** — instead, Steam functions are exposed from C# via GameAPI
function pointers. This avoids conflicts with the game's already-initialized Steam instance.

### C# (new assembly references)
| Assembly | Purpose | Status |
|----------|---------|--------|
| `UnityEngine.AIModule` | NavMeshAgent removal on remote player models | ✅ Added |
| `UnityEngine.UIModule` | Canvas + RenderMode for nametags | ✅ Added |

All other deps are existing: MelonLoader, Il2CppInterop, Harmony, Unity.TextMeshPro, Steamworks (via direct DllImport to steam_api64.dll).

## Risk Assessment

| Risk | Impact | Mitigation | Status |
|------|--------|------------|--------|
| ~~`steamworks` crate conflicts with game's `steam_api64.dll`~~ | ~~Blocks Phase 1~~ | ~~Fallback: raw FFI~~ | ✅ **Avoided** — using DllImport to steam_api64.dll directly from C# |
| `AcceptP2PSessionWithUser` needs callback to accept incoming | Blocks P2P connection | Pre-accept both sides. If fails, add Harmony patch on SteamManager.Update to handle P2PSessionRequest_t | 🔶 Needs runtime test |
| Game updates break Harmony patches | Breaks Phase 2-3 | Defensive patching with try/catch. Version-pin game. | — |
| Two players modifying same object simultaneously | Desyncs in Phase 3 | Object locking: "player X is interacting with rack Y" broadcast. | — |
| Bandwidth with 4 players | Lag in Phase 4 | Start with 2, profile, optimize. Delta compression for state sync. | — |
| IL2CPP object spawning on client is unreliable | Blocks Phase 2 | Test prefab instantiation early. Fallback: trigger game's own spawn methods. | — |
| Save corruption from multiplayer state | Data loss | Never modify save format. Multiplayer state is separate from game saves. | — |
| SteamAPI interface version strings wrong (v006, v023, v017) | DllImport returns null | Check actual version strings in steam_api64.dll exports. Fallback: use SteamInternal_FindOrCreateUserInterface. | 🔶 Needs runtime test |

## Open Questions

- [x] ~~Can `steamworks` crate coexist with game's already-initialized Steam instance?~~ → **Avoided entirely. Using DllImport.**
- [x] ~~How to get Steam friend's display name for nametags?~~ → `SteamAPI_ISteamFriends_GetFriendPersonaName` via DllImport
- [ ] Which technician prefab index looks best as a player model? (currently using index 0)
- [ ] Does pre-calling `AcceptP2PSessionWithUser` work without the P2PSessionRequest_t callback?
- [ ] Are the Steam API interface version strings correct? (SteamNetworking_v006, SteamUser_v023, SteamFriends_v017)
- [ ] Can we intercept `InputManager` to block client actions cleanly? (Phase 3)
- [ ] How does the game handle multiple `FirstPersonController` instances? (Phase 2 — remote players don't use FPC)
- [ ] What happens to game pause when client is in a menu? (Phase 3)
- [ ] How to handle object UIDs across host/client? (Phase 3 — host assigns, clients use host's UIDs)
- [ ] How to trigger `mp_connect(steam_id)` from in-game? (Need UI or chat command system)

## Quick Reference: How to Continue

When starting a new thread about multiplayer development, paste this context:

> I'm building multiplayer for the Data Center modloader. See `docs/multiplayer_roadmap.md`
> for all design decisions and the phased plan. Current progress: Phase 1a scaffolding DONE,
> needs runtime testing. The modloader repo is at `E:\rust\data center modloader`.
> Architecture: Host-Client, Steam P2P (DllImport to steam_api64.dll), Rust-heavy with thin C# relay.
> Key files: `claude.md` (project knowledge base), `docs/multiplayer_roadmap.md` (this roadmap).
>
> Key crates: `dc_multiplayer` (multiplayer mod), `dc_api` (FFI v7 with Steam wrappers).
> Key C# files: `GameAPI.cs` (v7 function pointer table), `MultiplayerBridge.cs` (remote player rendering).