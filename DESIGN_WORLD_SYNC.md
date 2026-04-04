# World Object Synchronization — Design Document

> **Status:** Planning complete, implementation not started
> **Last updated:** 2025-01-XX
> **Scope:** Synchronizing world object state (placement, removal, configuration) between host and clients in multiplayer sessions.

---

## Table of Contents

1. [Context & Goals](#1-context--goals)
2. [Architecture Overview](#2-architecture-overview)
3. [Design Decisions (Decision Log)](#3-design-decisions-decision-log)
4. [Protocol Design](#4-protocol-design)
5. [FFI Interface (C# ↔ Rust)](#5-ffi-interface-c--rust)
6. [Event System Extensions](#6-event-system-extensions)
7. [World State Tracking (Rust)](#7-world-state-tracking-rust)
8. [Host Logic](#8-host-logic)
9. [Client Logic](#9-client-logic)
10. [Hash-Check Safety Net](#10-hash-check-safety-net)
11. [Conflict Resolution](#11-conflict-resolution)
12. [Implementation Plan](#12-implementation-plan)
13. [File Map — Where Things Live](#13-file-map--where-things-live)
14. [Open Questions / Future Work](#14-open-questions--future-work)

---

## 1. Context & Goals

### What we already have (before this feature)
- Position + rotation sync at 20 Hz with interpolation
- Walk/run animation sync (speed-based)
- Nametags above remote player heads
- Join state machine (Idle → WaitingForSave → SaveReady → LoadingScene → Loaded)
- Save file transfer from host to joining client (chunked, hash-compared)
- Player visual state sync (carry type, crouching, sitting)
- Relay server over WebSocket (no direct P2P)
- Host-authority model for save files

### What this feature adds
Synchronization of **world object state** between players during gameplay. When one player places a server in a rack, connects a cable, toggles power, etc., all other players see the change in real-time.

### What this feature does NOT cover
- Player character visual states (carry animations, crouching on remote players) — separate feature (Point A)
- NPC/Technician synchronization
- Shop UI synchronization (each player sees their own shop)
- Economy synchronization (money, XP) — future feature

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        CLIENT A (Actor)                         │
│                                                                 │
│  1. Player performs action (e.g., installs server in rack)      │
│  2. Action executes LOCALLY immediately (optimistic)            │
│  3. C# Harmony Patch fires Event (ServerInstalled)              │
│  4. Rust mod_on_event receives it                               │
│  5. Rust creates WorldAction { seq: N, action } message         │
│  6. Sends via Relay to Host                                     │
│  7. Starts 5s ACK timeout                                       │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
                 ┌───────────────────┐
                 │   RELAY SERVER    │
                 │  (WebSocket Hub)  │
                 └────────┬──────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                          HOST                                   │
│                                                                 │
│  1. Receives WorldAction { seq: N, action }                     │
│  2. Validates (is the action legal right now?)                  │
│  3a. VALID:                                                     │
│      → Send WorldActionAck { seq: N, accepted: true } to A     │
│      → Send WorldActionBroadcast { action } to ALL OTHERS      │
│  3b. INVALID:                                                   │
│      → Send WorldActionAck { seq: N, accepted: false } to A    │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                     CLIENT B (Observer)                          │
│                                                                 │
│  1. Receives WorldActionBroadcast { action }                    │
│  2. Rust calls C# FFI to execute the action visually            │
│     e.g., world_place_in_rack("SVR_01", 42)                    │
│  3. Game object appears in rack                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Host's own actions (simplified path)
```
HOST performs action locally
  → Action executes immediately
  → Harmony Patch fires Event
  → Rust creates WorldActionBroadcast { action }
  → Sends to ALL clients
  → No seq, no ACK, no timeout (host IS the authority)
```

### Safety net (periodic, every ~20 seconds)
```
HOST:
  1. C# collects {object_id, mini_hash} for every world object
  2. Rust sends WorldHashCheck to all clients

CLIENT:
  1. Compares each hash with local state
  2. Mismatch → sends WorldResyncRequest { object_id }
  3. Host responds with WorldResyncResponse { object_id, full_state }
  4. Client applies the corrected state
```

---

## 3. Design Decisions (Decision Log)

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| 1 | **Authority model** | Optimistic-local + Host-broadcast | Client actions feel instant (0ms local delay). Host validates and broadcasts. Rollback on reject. Round-trip latency (~300ms via relay) would feel sluggish for host-authoritative with client waiting. |
| 2 | **Sync strategy** | Events (primary) + Hash-check safety net (secondary) | Events handle 99% of cases with instant propagation. Hash-check every ~20s catches any missed events (self-healing). |
| 3 | **Hash-check variant** | Hash-list + on-demand resync (Variante 2) | Minimal bandwidth in happy path. Host sends compact hash list, client compares locally. Only fetches full state for mismatched objects. |
| 4 | **Object IDs** | Use game's native IDs | Server → `String serverID`, Switch → `String switchID`, PatchPanel → `String patchPanelID`, Cable → `Int32 cableID`. Generated via `NetworkMap.GenerateDeviceName(TypeOfLink, Vector3)`. SFP modules get synthetic IDs (no native ID). |
| 5 | **Message structure** | Encapsulated `WorldAction` sub-enum | Keeps the main `Message` enum clean. Three channel types: `WorldAction` (request), `WorldActionAck` (response), `WorldActionBroadcast` (authoritative). |
| 6 | **ACK system** | Per-action `seq` number + ACK/reject | Client assigns incrementing `seq` per action. Host references `seq` in ACK. Client can correlate which action was accepted/rejected. |
| 7 | **Timeout** | 5 seconds, then rollback | If no ACK after 5s → roll back the optimistic local action. Hash-check will correct if we rolled back incorrectly. |
| 8 | **Host's own actions** | Direct broadcast, no self-ACK | Host is authority — no need to request permission from itself. Just execute + broadcast. |
| 9 | **Conflict resolution** | Broadcast always wins | If a client holds an object and receives a broadcast saying that object was placed elsewhere → object leaves the client's hand. Host authority is absolute. |
| 10 | **FFI granularity** | Fine-grained functions | One C# function per action type. Easier to debug, each function is a one-liner on C# side. Synchronous return codes (1=OK, 0=fail). |

---

## 4. Protocol Design

### New Message variants (in `protocol.rs`)

```rust
#[derive(Encode, Decode, Debug, Clone)]
pub enum Message {
    // ── Existing messages (unchanged) ──────────────────────────
    Position { x: f32, y: f32, z: f32, rot_y: f32 },
    Hello { player_name: String, mod_version: String },
    Welcome { player_name: String, is_host: bool, spawn_x: f32, spawn_y: f32, spawn_z: f32 },
    Goodbye,
    Ping(u64),
    Pong(u64),
    RequestSave,
    SaveOffer { total_bytes: u32, chunk_count: u32, save_hash: u64 },
    SaveChunk { index: u32, data: Vec<u8> },
    SaveSkip,
    PlayerState { object_in_hand: u8, num_objects: u8, is_crouching: bool, is_sitting: bool },

    // ── NEW: World Object Sync ─────────────────────────────────

    /// Client → Host: "I performed this action" (with sequence number for ACK tracking)
    WorldAction {
        seq: u32,
        action: WorldAction,
    },

    /// Host → originating Client: "Your action was accepted/rejected"
    WorldActionAck {
        seq: u32,
        accepted: bool,
    },

    /// Host → all OTHER clients: "This action happened (authoritative)"
    WorldActionBroadcast {
        action: WorldAction,
    },

    /// Host → all clients: periodic hash list for desync detection
    WorldHashCheck {
        hashes: Vec<ObjectHash>,
    },

    /// Client → Host: "My state for this object doesn't match, send me the full state"
    WorldResyncRequest {
        object_id: String,
    },

    /// Host → requesting Client: "Here's the full authoritative state of that object"
    WorldResyncResponse {
        object_id: String,
        object_type: u8,
        data: Vec<u8>,  // serialized full object state
    },
}
```

### WorldAction enum

```rust
#[derive(Encode, Decode, Debug, Clone)]
pub enum WorldAction {
    /// Player picked up an object from the world (object disappears from world)
    ObjectPickedUp {
        object_id: String,
        object_type: u8,    // maps to ObjectInHand enum
    },

    /// Player dropped/placed an object in the world
    ObjectDropped {
        object_id: String,
        object_type: u8,
        pos_x: f32,
        pos_y: f32,
        pos_z: f32,
        rot_x: f32,
        rot_y: f32,
        rot_z: f32,
        rot_w: f32,
    },

    /// Object installed into a rack slot
    InstalledInRack {
        object_id: String,
        object_type: u8,
        rack_position_uid: i32,
    },

    /// Object removed from a rack slot
    RemovedFromRack {
        object_id: String,
        object_type: u8,
    },

    /// Server/Switch power toggled
    PowerToggled {
        object_id: String,
        is_on: bool,
    },

    /// Generic property change (IP, customer, label, etc.)
    PropertyChanged {
        object_id: String,
        key: String,       // e.g. "ip", "customer_id", "label", "app_id"
        value: String,     // string-encoded value
    },

    /// Cable connected between two endpoints
    CableConnected {
        cable_id: i32,
        start_type: u8,       // TypeOfLink enum value
        start_pos_x: f32,
        start_pos_y: f32,
        start_pos_z: f32,
        start_device_id: String,
        end_type: u8,
        end_pos_x: f32,
        end_pos_y: f32,
        end_pos_z: f32,
        end_device_id: String,
    },

    /// Cable disconnected/removed
    CableDisconnected {
        cable_id: i32,
    },

    /// New object spawned into the world (e.g., shop delivery, or SFP from box)
    ObjectSpawned {
        object_id: String,
        object_type: u8,
        prefab_id: i32,
        pos_x: f32,
        pos_y: f32,
        pos_z: f32,
        rot_x: f32,
        rot_y: f32,
        rot_z: f32,
        rot_w: f32,
    },

    /// Object permanently destroyed (e.g., thrown in dumpster)
    ObjectDestroyed {
        object_id: String,
        object_type: u8,
    },
}
```

### ObjectHash (for hash-check messages)

```rust
#[derive(Encode, Decode, Debug, Clone)]
pub struct ObjectHash {
    pub object_id: String,
    pub object_type: u8,
    pub hash: u32,
}
```

### Object type constants (matching `ObjectInHand` enum from the game)

```rust
pub mod object_types {
    pub const NONE: u8 = 0;
    pub const SERVER_1U: u8 = 1;
    pub const SERVER_7U: u8 = 2;
    pub const SERVER_3U: u8 = 3;
    pub const SWITCH: u8 = 4;
    pub const RACK: u8 = 5;
    pub const CABLE_SPINNER: u8 = 6;
    pub const PATCH_PANEL: u8 = 7;
    pub const SFP_MODULE: u8 = 8;
    pub const SFP_BOX: u8 = 9;
}
```

### Reliability

All `WorldAction`, `WorldActionAck`, `WorldActionBroadcast`, and hash-check messages are **reliable** (they must arrive). They go through the relay as `RelayPacket::GameData`, which uses TCP (WebSocket), so delivery is guaranteed.

---

## 5. FFI Interface (C# ↔ Rust)

All new functions follow the existing pattern in `GameAPI` struct (`dc_api/src/lib.rs`).
C# implements these as thin wrappers calling into game objects via IL2CPP.

### Group 1: Read Functions (for hash-check)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Function                              │ Signature                          │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_get_object_count                │ () → u32                          │
│   Returns total number of syncable    │                                    │
│   world objects (servers + switches   │                                    │
│   + patch panels + cables + SFPs)     │                                    │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_get_object_hashes              │ (buf: *mut ObjectHashFFI,          │
│   Fills buffer with {id, type, hash}  │  max_count: u32) → u32           │
│   for every world object.             │  Returns count written.            │
│   Hash = hash of (pos, rot, rackUID,  │                                    │
│   isOn, isBroken, key properties)     │                                    │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_get_object_state               │ (id: *const u8, id_len: u32,      │
│   Returns the full serialized state   │  buf: *mut u8, buf_max: u32)      │
│   of a single object (for resync).    │  → u32 (bytes written)            │
└───────────────────────────────────────┴────────────────────────────────────┘
```

#### ObjectHashFFI layout (C-compatible struct)

```rust
#[repr(C)]
pub struct ObjectHashFFI {
    pub object_id: [u8; 64],    // null-terminated UTF-8 string
    pub object_id_len: u32,
    pub object_type: u8,
    pub hash: u32,
}
```

### Group 2: Write Functions (for applying remote actions)

All return `i32`: 1 = success, 0 = failure.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Function                              │ Signature                          │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_spawn_object                   │ (object_type: u8, prefab_id: i32, │
│   Spawns a new object at position.    │  x: f32, y: f32, z: f32,         │
│   Returns object ID in out buffer.    │  rot_x: f32, rot_y: f32,         │
│                                       │  rot_z: f32, rot_w: f32,         │
│                                       │  out_id: *mut u8, out_max: u32)  │
│                                       │  → i32                            │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_destroy_object                 │ (id: *const u8, id_len: u32)      │
│   Permanently removes object.         │  → i32                            │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_place_in_rack                  │ (id: *const u8, id_len: u32,      │
│   Installs object into rack slot.     │  rack_uid: i32) → i32            │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_remove_from_rack               │ (id: *const u8, id_len: u32)      │
│   Removes object from its rack slot.  │  → i32                            │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_set_power                      │ (id: *const u8, id_len: u32,      │
│   Toggles power on server/switch.     │  is_on: u8) → i32                │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_set_property                   │ (id: *const u8, id_len: u32,      │
│   Sets a named property.              │  key: *const u8, key_len: u32,   │
│   Keys: "ip", "customer_id",         │  val: *const u8, val_len: u32)   │
│   "label", "app_id"                  │  → i32                            │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_connect_cable                  │ (cable_id: i32,                   │
│   Connects a cable between endpoints. │  start_type: u8,                  │
│                                       │  sx: f32, sy: f32, sz: f32,      │
│                                       │  start_device: *const u8,         │
│                                       │  start_device_len: u32,           │
│                                       │  end_type: u8,                    │
│                                       │  ex: f32, ey: f32, ez: f32,      │
│                                       │  end_device: *const u8,           │
│                                       │  end_device_len: u32) → i32      │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_disconnect_cable               │ (cable_id: i32) → i32             │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_pickup_object                  │ (id: *const u8, id_len: u32)      │
│   Removes object from world           │  → i32                            │
│   (as if a remote player picked it    │                                    │
│   up — object disappears visually).   │                                    │
├───────────────────────────────────────┼────────────────────────────────────┤
│ world_drop_object                    │ (id: *const u8, id_len: u32,      │
│   Places object back into world at    │  x: f32, y: f32, z: f32,         │
│   position (as if remote player       │  rot_x: f32, rot_y: f32,         │
│   dropped it).                        │  rot_z: f32, rot_w: f32) → i32   │
└───────────────────────────────────────┴────────────────────────────────────┘
```

### Where to add these

- **Rust side:** Add function pointers to `GameAPI` struct in `crates/dc_api/src/lib.rs` (lines ~206-360)
- **Rust wrappers:** Add safe wrapper methods to `impl Api` in `crates/dc_api/src/lib.rs` (lines ~372-1229)
- **C# side:** Implement in the ModLoader's FFI bridge (same pattern as existing functions like `get_player_position`, `spawn_character`, etc.)

---

## 6. Event System Extensions

### Existing events that need richer payloads

The event system lives in `crates/dc_api/src/events/`. Events are fired from C# Harmony patches and decoded in `mod.rs`.

| Event (in `event_id.rs`) | Current Payload | Needed Payload |
|---------------------------|-----------------|----------------|
| `ServerInstalled` (ID 206) | *none* | `server_id: String`, `rack_position_uid: i32`, `prefab_id: i32` |
| `ServerPowered` (ID 200) | `powered_on: bool` | + `server_id: String` |
| `CableConnected` (ID 207) | *none* | `cable_id: i32`, start/end positions + types + device IDs |
| `CableDisconnected` (ID 208) | *none* | `cable_id: i32` |
| `RackUnmounted` (ID 211) | *none* | `object_id: String`, `object_type: u8` |
| `ServerCustomerChanged` (ID 209) | `customer_id: i32` | + `server_id: String` |
| `ServerAppChanged` (ID 210) | `app_id: i32` | + `server_id: String` |
| `WallPurchased` (ID 800) | *none* | `wall_index: i32` |

### New events to add

| Event | ID (proposed) | Payload |
|-------|---------------|---------|
| `ObjectPickedUp` | 212 | `object_id: String`, `object_type: u8` |
| `ObjectDropped` | 213 | `object_id: String`, `object_type: u8`, `pos: Vec3`, `rot: Quaternion` |
| `ObjectSpawned` | 214 | `object_id: String`, `object_type: u8`, `prefab_id: i32`, `pos: Vec3`, `rot: Quaternion` |
| `ObjectDestroyed` | 215 | `object_id: String`, `object_type: u8` |
| `SwitchInstalled` | 216 | `switch_id: String`, `rack_position_uid: i32` |
| `PatchPanelInstalled` | 217 | `patch_panel_id: String`, `rack_position_uid: i32` |
| `SFPInserted` | 218 | `sfp_id: String`, `port_position: Vec3` |

### Where to add these

- **Event IDs:** `crates/dc_api/src/events/event_id.rs`
- **Payload structs:** `crates/dc_api/src/events/payload.rs`
- **Event enum variants:** `crates/dc_api/src/events/event.rs`
- **Decode logic:** `crates/dc_api/src/events/mod.rs` (in the `decode` function)
- **C# Harmony Patches:** In the ModLoader C# project (extend existing patches, add new ones)

### How events flow into WorldAction messages

In `dc_multiplayer`, the `mod_on_event` handler (or a dedicated function called from it) converts game events into `WorldAction` messages:

```rust
// In dc_multiplayer, pseudocode:
fn on_game_event(event: Event) {
    let action = match event {
        Event::ServerInstalled { server_id, rack_position_uid, .. } => {
            WorldAction::InstalledInRack {
                object_id: server_id,
                object_type: object_types::SERVER_1U, // or whichever type
                rack_position_uid,
            }
        }
        Event::ObjectPickedUp { object_id, object_type } => {
            WorldAction::ObjectPickedUp { object_id, object_type }
        }
        // ... etc
        _ => return, // not a world-sync event
    };

    let is_host = with_state(|s| s.is_host).unwrap_or(false);
    if is_host {
        // Host: broadcast directly
        send_broadcast(WorldActionBroadcast { action });
    } else {
        // Client: send request with seq
        let seq = next_seq();
        register_pending_action(seq, action.clone(), rollback_info);
        send_to_host(WorldAction { seq, action });
    }
}
```

---

## 7. World State Tracking (Rust)

### New module: `crates/dc_multiplayer/src/world.rs`

This module tracks:
1. **Pending actions** — actions the local client performed optimistically, awaiting ACK
2. **Sequence counter** — incrementing `seq` for outgoing actions
3. **Timeout tracking** — 5-second timer per pending action

```rust
pub struct PendingAction {
    pub seq: u32,
    pub action: WorldAction,
    pub sent_at: f32,          // game time when sent
    pub rollback_info: RollbackInfo,  // data needed to undo the action
}

pub enum RollbackInfo {
    /// Object was picked up — rollback = drop it back
    UndoPickup {
        object_id: String,
        object_type: u8,
        original_pos: (f32, f32, f32),
        original_rot: (f32, f32, f32, f32),
    },
    /// Object was dropped — rollback = pick it back up (remove from world)
    UndoDrop {
        object_id: String,
    },
    /// Object was installed in rack — rollback = remove from rack, place at previous pos
    UndoInstall {
        object_id: String,
        object_type: u8,
        previous_pos: (f32, f32, f32),
        previous_rot: (f32, f32, f32, f32),
    },
    /// Object was removed from rack — rollback = put back in rack
    UndoRemoveFromRack {
        object_id: String,
        object_type: u8,
        rack_position_uid: i32,
    },
    /// Power was toggled — rollback = toggle back
    UndoPowerToggle {
        object_id: String,
        was_on: bool,
    },
    /// Property was changed — rollback = set old value
    UndoPropertyChange {
        object_id: String,
        key: String,
        old_value: String,
    },
    /// Cable was connected — rollback = disconnect
    UndoCableConnect {
        cable_id: i32,
    },
    /// Cable was disconnected — rollback = reconnect (store full endpoint info)
    UndoCableDisconnect {
        cable_id: i32,
        // store full CableConnected data for re-connection
        start_type: u8,
        start_pos: (f32, f32, f32),
        start_device_id: String,
        end_type: u8,
        end_pos: (f32, f32, f32),
        end_device_id: String,
    },
    /// Object was spawned — rollback = destroy
    UndoSpawn {
        object_id: String,
    },
    /// Object was destroyed — rollback = respawn
    UndoDestroy {
        object_id: String,
        object_type: u8,
        prefab_id: i32,
        pos: (f32, f32, f32),
        rot: (f32, f32, f32, f32),
    },
    /// No rollback needed/possible
    None,
}

pub struct WorldSyncState {
    /// Next sequence number for outgoing actions
    pub next_seq: u32,

    /// Actions awaiting ACK from host (client only)
    pub pending_actions: Vec<PendingAction>,

    /// Timer for hash-check broadcasts (host only)
    pub hash_check_timer: f32,

    /// Last known object hashes (client, for comparison)
    pub last_known_hashes: HashMap<String, u32>,
}
```

### Where to add this state

Add `WorldSyncState` as a field in `MultiplayerState` (in `crates/dc_multiplayer/src/state.rs`, line ~60):

```rust
pub struct MultiplayerState {
    // ... existing fields ...

    /// World object synchronization state
    pub world_sync: WorldSyncState,
}
```

### Constants (in `state.rs`)

```rust
pub const WORLD_ACTION_TIMEOUT_SECS: f32 = 5.0;
pub const HASH_CHECK_INTERVAL_SECS: f32 = 20.0;
```

---

## 8. Host Logic

### Location: `crates/dc_multiplayer/src/handlers.rs`

Add a new handler in `handle_message` for `Message::WorldAction`:

```rust
Message::WorldAction { seq, action } => {
    if !is_host { return; }  // only host processes these

    let accepted = validate_world_action(&action);

    // Send ACK to the requesting client
    let ack = Message::WorldActionAck { seq, accepted };
    send_to(sender, ack);

    if accepted {
        // Execute the action on host side (if not already done — see note below)
        // Note: If the host receives an action from a client, the host does NOT
        // need to execute it locally — the action only affects the CLIENT's world.
        // The host just validates and broadcasts.
        //
        // BUT: If the action creates server-side state (like generating an ID),
        // the host needs to handle that.

        // Broadcast to all OTHER clients (not the sender, they already did it optimistically)
        broadcast_to_others(sender, Message::WorldActionBroadcast { action });
    }
}
```

### Validation function

```rust
fn validate_world_action(action: &WorldAction) -> bool {
    // For Phase 2, start with simple validation:
    match action {
        WorldAction::InstalledInRack { object_id, rack_position_uid, .. } => {
            // Check: does the object exist? Is the rack slot empty?
            // Call into GameAPI to verify
            true // placeholder
        }
        WorldAction::RemovedFromRack { object_id, .. } => {
            // Check: is the object actually in a rack?
            true
        }
        // ... etc
        _ => true  // default: accept
    }
}
```

### Host's own actions

When the host performs an action and receives the game event in `mod_on_event`:

```rust
// In the event→WorldAction conversion:
if is_host {
    // Don't send WorldAction (that's for clients requesting).
    // Just broadcast directly:
    let broadcast = Message::WorldActionBroadcast { action };
    send_to_all_clients(broadcast);
    // No seq, no pending action, no timeout.
}
```

---

## 9. Client Logic

### Receiving WorldActionAck

In `handlers.rs`, add handler:

```rust
Message::WorldActionAck { seq, accepted } => {
    if is_host { return; }  // host doesn't receive ACKs

    with_state(|s| {
        if let Some(idx) = s.world_sync.pending_actions.iter().position(|p| p.seq == seq) {
            let pending = s.world_sync.pending_actions.remove(idx);
            if !accepted {
                // ROLLBACK: undo the optimistic action
                execute_rollback(api, &pending.rollback_info);
                api.show_notification("Action was rejected by host.");
            }
            // If accepted: nothing to do, the optimistic action was correct.
        }
    });
}
```

### Receiving WorldActionBroadcast

In `handlers.rs`, add handler:

```rust
Message::WorldActionBroadcast { action } => {
    // This is an authoritative action from the host.
    // Execute it locally via FFI.
    execute_world_action(api, &action);
}
```

### execute_world_action (calls C# FFI)

```rust
fn execute_world_action(api: &Api, action: &WorldAction) {
    match action {
        WorldAction::InstalledInRack { object_id, object_type, rack_position_uid } => {
            api.world_place_in_rack(object_id, *rack_position_uid);
        }
        WorldAction::RemovedFromRack { object_id, .. } => {
            api.world_remove_from_rack(object_id);
        }
        WorldAction::PowerToggled { object_id, is_on } => {
            api.world_set_power(object_id, *is_on);
        }
        WorldAction::ObjectPickedUp { object_id, .. } => {
            api.world_pickup_object(object_id);
        }
        WorldAction::ObjectDropped { object_id, pos_x, pos_y, pos_z, rot_x, rot_y, rot_z, rot_w, .. } => {
            api.world_drop_object(object_id, *pos_x, *pos_y, *pos_z, *rot_x, *rot_y, *rot_z, *rot_w);
        }
        // ... etc for all action types
    }
}
```

### Timeout check (in tick loop)

In `tick.rs` or `world.rs`, called every frame:

```rust
fn check_pending_action_timeouts(api: &Api, game_time: f32) {
    with_state(|s| {
        let timed_out: Vec<PendingAction> = s.world_sync.pending_actions
            .drain_filter(|p| game_time - p.sent_at > WORLD_ACTION_TIMEOUT_SECS)
            .collect();

        for pending in timed_out {
            dc_api::crash_log(&format!(
                "[WORLD] Action seq={} timed out after 5s, rolling back",
                pending.seq
            ));
            execute_rollback(api, &pending.rollback_info);
        }
    });
}
```

---

## 10. Hash-Check Safety Net

### Host-side (periodic broadcast)

In `tick.rs`, add to the update loop (host only):

```rust
// Only host sends hash checks
if is_host {
    with_state(|s| {
        s.world_sync.hash_check_timer += dt;
        if s.world_sync.hash_check_timer >= HASH_CHECK_INTERVAL_SECS {
            s.world_sync.hash_check_timer = 0.0;
            true
        } else {
            false
        }
    });

    if should_hash_check {
        // Read object hashes from C# via FFI
        let hashes = api.world_get_object_hashes();  // returns Vec<ObjectHash>

        let msg = Message::WorldHashCheck { hashes };
        broadcast_to_all(msg);
    }
}
```

### Client-side (comparison + resync request)

In `handlers.rs`:

```rust
Message::WorldHashCheck { hashes } => {
    if is_host { return; }

    // Get our local hashes
    let local_hashes = api.world_get_object_hashes();
    let local_map: HashMap<String, u32> = local_hashes.iter()
        .map(|h| (h.object_id.clone(), h.hash))
        .collect();
    let remote_map: HashMap<String, u32> = hashes.iter()
        .map(|h| (h.object_id.clone(), h.hash))
        .collect();

    // Check for mismatches
    for (id, remote_hash) in &remote_map {
        match local_map.get(id) {
            Some(local_hash) if local_hash == remote_hash => {
                // Match — all good
            }
            _ => {
                // Mismatch or missing locally — request resync
                dc_api::crash_log(&format!("[WORLD] Hash mismatch for {}, requesting resync", id));
                send_to_host(Message::WorldResyncRequest { object_id: id.clone() });
            }
        }
    }

    // Check for objects we have but host doesn't (should be deleted)
    for (id, _) in &local_map {
        if !remote_map.contains_key(id) {
            dc_api::crash_log(&format!("[WORLD] Object {} exists locally but not on host, removing", id));
            api.world_destroy_object(id);
        }
    }
}
```

### Host responds to resync request

```rust
Message::WorldResyncRequest { object_id } => {
    if !is_host { return; }

    // Get full state of the requested object
    let (object_type, data) = api.world_get_object_state(&object_id);

    let response = Message::WorldResyncResponse {
        object_id,
        object_type,
        data,
    };
    send_to(sender, response);
}
```

### Client applies resync response

```rust
Message::WorldResyncResponse { object_id, object_type, data } => {
    if is_host { return; }

    // Destroy local version if it exists
    api.world_destroy_object(&object_id);

    // Deserialize and recreate from the authoritative data
    apply_full_object_state(api, &object_id, object_type, &data);

    dc_api::crash_log(&format!("[WORLD] Resynced object {}", object_id));
}
```

---

## 11. Conflict Resolution

### Rule: Broadcast always wins (Host authority is absolute)

When a client receives a `WorldActionBroadcast` for an object it currently has in a "pending" state (waiting for ACK on its own action for that object):

```rust
fn execute_world_action(api: &Api, action: &WorldAction) {
    let object_id = action.object_id();  // helper to extract ID from any variant

    // If we have a pending action for this same object, the broadcast overrides it.
    with_state(|s| {
        s.world_sync.pending_actions.retain(|p| {
            if p.action.object_id() == object_id {
                dc_api::crash_log(&format!(
                    "[WORLD] Broadcast overrides pending action seq={} for {}",
                    p.seq, object_id
                ));
                // Don't rollback — the broadcast itself IS the correction
                false  // remove from pending
            } else {
                true   // keep
            }
        });
    });

    // Now execute the broadcast action
    match action {
        // ... (same as section 9)
    }
}
```

### Edge case: Object in local player's hand

If the local player is holding an object and a broadcast says that object was placed/installed somewhere:

```rust
WorldAction::InstalledInRack { object_id, .. } |
WorldAction::ObjectDropped { object_id, .. } => {
    // Check if we're holding this object
    let carry_state = api.get_player_carry_state();
    // If our local player has this object → force-drop it
    // (The GameAPI would need a force_drop_held_object function,
    //  or we handle this in C# when world_place_in_rack is called
    //  for an object the local player holds)
}
```

---

## 12. Implementation Plan

### Phase 1 — Foundation (no networking, testable in isolation)

**Goal:** All data structures and FFI interfaces defined. Compiles and can be tested with mock data.

| Task | File(s) | Description |
|------|---------|-------------|
| 1.1 | `crates/dc_multiplayer/src/protocol.rs` | Add `WorldAction` enum, `ObjectHash`, new `Message` variants |
| 1.2 | `crates/dc_multiplayer/src/world.rs` (NEW) | `PendingAction`, `RollbackInfo`, `WorldSyncState` structs |
| 1.3 | `crates/dc_multiplayer/src/state.rs` | Add `world_sync: WorldSyncState` to `MultiplayerState`, add constants |
| 1.4 | `crates/dc_multiplayer/src/lib.rs` | Add `mod world;` |
| 1.5 | `crates/dc_api/src/lib.rs` | Add new function pointers to `GameAPI`, add safe wrappers to `impl Api` |
| 1.6 | C# ModLoader | Implement the FFI functions (stubs first, then real implementations) |

**Acceptance criteria:** `cargo build` succeeds. Unit tests for serialization/deserialization of new message types pass.

### Phase 2 — First End-to-End Roundtrip

**Goal:** One action type (ServerInstalled) works end-to-end: Host installs server → Client sees it. Client installs server → Host ACKs → other clients see it.

| Task | File(s) | Description |
|------|---------|-------------|
| 2.1 | `crates/dc_api/src/events/` | Extend `ServerInstalled` event with `server_id`, `rack_position_uid` |
| 2.2 | C# Harmony Patches | Update `ServerInstalled` patch to include server ID and rack UID |
| 2.3 | `crates/dc_multiplayer/src/handlers.rs` | Handle `WorldAction`, `WorldActionAck`, `WorldActionBroadcast` messages |
| 2.4 | `crates/dc_multiplayer/src/tick.rs` or `world.rs` | Event → WorldAction conversion, sending logic |
| 2.5 | C# ModLoader | Implement `world_place_in_rack` for real |
| 2.6 | **TEST** | Two players: both install servers, verify sync |

**Acceptance criteria:** Host installs server → client sees it appear in rack within ~300ms. Client installs server → Host ACKs → client keeps it → other clients see it.

### Phase 3 — All Action Types

**Goal:** All world actions synchronize correctly.

| Task | File(s) | Description |
|------|---------|-------------|
| 3.1 | Events + Harmony Patches | Add all remaining events (`ObjectPickedUp`, `ObjectDropped`, `CableConnected`, etc.) |
| 3.2 | `handlers.rs` | Handle all `WorldAction` variants |
| 3.3 | C# ModLoader | Implement all remaining FFI write functions |
| 3.4 | `tick.rs` / `world.rs` | Timeout tracking + rollback execution |
| 3.5 | **TEST** | Full gameplay session with all action types |

**Acceptance criteria:** All actions in the table from the design sync correctly. Rollbacks work on timeout.

### Phase 4 — Hash-Check Safety Net

**Goal:** Periodic desync detection and automatic correction.

| Task | File(s) | Description |
|------|---------|-------------|
| 4.1 | C# ModLoader | Implement `world_get_object_hashes` and `world_get_object_state` |
| 4.2 | `crates/dc_api/src/lib.rs` | Wrappers for hash/state read functions |
| 4.3 | `tick.rs` | Host: periodic hash broadcast. Client: comparison logic. |
| 4.4 | `handlers.rs` | `WorldHashCheck`, `WorldResyncRequest`, `WorldResyncResponse` handlers |
| 4.5 | **TEST** | Intentionally desync (drop a network packet) → verify auto-correction within ~20s |

**Acceptance criteria:** Artificially introduced desyncs are automatically corrected within one hash-check interval.

---

## 13. File Map — Where Things Live

### Rust crate: `dc_multiplayer` (`crates/dc_multiplayer/src/`)

| File | Purpose | What to change |
|------|---------|----------------|
| `protocol.rs` | Network message definitions | Add `WorldAction`, `ObjectHash`, new `Message` variants |
| `world.rs` | **NEW** — World sync state, pending actions, rollback logic | Create from scratch |
| `state.rs` | Global multiplayer state | Add `WorldSyncState` field, new constants |
| `handlers.rs` | Message processing | Add handlers for all new message types |
| `tick.rs` | Per-frame update loop | Add world action sending, timeout checks, hash-check timing |
| `ffi.rs` | FFI exports for C# | May need new exports if C# needs to push world events to Rust |
| `lib.rs` | Module declarations | Add `mod world;` |
| `save.rs` | Save transfer | Unchanged |
| `net.rs` | WebSocket relay connection | Unchanged |
| `player.rs` | Remote player tracking | Unchanged |

### Rust crate: `dc_api` (`crates/dc_api/src/`)

| File | Purpose | What to change |
|------|---------|----------------|
| `lib.rs` | GameAPI struct + Api wrapper | Add ~12 new function pointers + safe wrappers |
| `events/event_id.rs` | Event ID constants | Add new event IDs (212-218) |
| `events/event.rs` | Event enum | Add new variants with extended payloads |
| `events/payload.rs` | FFI payload structs | Add new `#[repr(C)]` structs for extended event data |
| `events/mod.rs` | Event decoding | Add decode cases for new/extended events |

### C# ModLoader (not in Rust workspace)

| Component | What to change |
|-----------|----------------|
| `GameAPI` FFI bridge | Add ~12 new function implementations |
| Harmony Patches | Extend existing patches (more payload data), add new patches (PickUp, Drop, Spawn, Destroy) |
| `EventIds.cs` | Add new event ID constants matching Rust |

### Relay server (`dc_relay_proto`)

| File | What to change |
|------|----------------|
| `src/lib.rs` | **Nothing** — relay is transport-agnostic, just forwards `GameData` payloads |

---

## 14. Open Questions / Future Work

### Open questions (to resolve during implementation)

1. **SFP Module IDs:** SFPs have no native ID. How to generate synthetic IDs? Proposal: `"SFP_{prefabID}_{x:.0}_{y:.0}_{z:.0}"` based on initial position.

2. **Cable waypoints:** `CableSaveData` has `List<waypoints>` and `List<midPointPositions>`. Do we need to sync these in `CableConnected`, or does the game auto-generate them from start/end positions?

3. **Shop delivery sync:** When a player buys items in the shop, objects spawn in the delivery area. The `ShopCheckout` event needs to be extended to include what was purchased, or we rely on the hash-check to pick up new objects.

4. **Object state serialization format:** For `world_get_object_state` (resync), what binary format? Options: (a) reuse the game's own `SaveData` serialization per-object, (b) custom `#[repr(C)]` structs, (c) bincode.

5. **Trolley position:** The trolley is a shared physics object. Should it be synced via WorldAction (explicit push events) or via periodic position sync (like player positions)?

6. **Rack doors:** `RackDoor : Interact` — trivial to sync (open/close), but is it worth the network traffic? Could be Phase 5.

### Future work (beyond this design)

- **Economy sync:** Money, XP, reputation changes need to be host-authoritative
- **Customer acceptance sync:** When host accepts a customer, clients need to see it
- **Technician/NPC sync:** Technicians moving around, repairing, replacing — complex animation sync
- **Undo/Redo:** If rollbacks become common, a proper undo stack might be needed
- **Bandwidth optimization:** Delta compression for hash-check lists, bitpacking for common actions
- **Latency compensation:** Predictive placement for observers (show action slightly before it's confirmed)

---

## Appendix A: Game Object Reference (from IL2CPP inspection)

### SaveData (root — `full_output.txt` L1605-1627)
```
SaveData._current
SaveData.playerData
SaveData.networkData              → NetworkSaveData
SaveData.rackMountObjectData      → List<RackMountObjectData>
SaveData.isWallOpened             → bool[]
SaveData.interactObjectData       → List<InteractObjectData>
SaveData.lastUsedRackPositionGlobalUID → int  (incrementing counter)
SaveData.wallPrice                → float
SaveData.trolleyPosition          → Vector3
SaveData.trolleyRotation          → Quaternion
```

### NetworkSaveData (`full_output.txt` L1191-1200)
```
NetworkSaveData.servers           → List<ServerSaveData>
NetworkSaveData.switches          → List<SwitchSaveData>
NetworkSaveData.patchPanels       → List<PatchPanelSaveData>
NetworkSaveData.cables            → List<CableSaveData>
NetworkSaveData.customerBases     → List<CustomerBaseSaveData>
NetworkSaveData.sfpModules        → List<SFPSaveData>
NetworkSaveData.lacpGroups        → List<LACPGroupSaveData>
```

### ServerSaveData (`full_output.txt` L1718-1733)
```
serverID          : String        ← UNIQUE ID (generated by GenerateDeviceName)
customerID        : int
ip                : String
serverType        : int
position          : Vector3
rotation          : Quaternion
rackPositionUID   : int           ← which rack slot (0 or -1 if not in rack?)
prefabID          : int
isOn              : bool
isBroken          : bool
timeToBrake       : int
eolTime           : int
isWarningCleared  : bool
```

### SwitchSaveData (`full_output.txt` L1905-1917)
```
switchID          : String        ← UNIQUE ID
switchType        : int
position          : Vector3
rotation          : Quaternion
rackPositionUID   : int
isOn              : bool
label             : String
isBroken          : bool
timeToBrake       : int
eolTime           : int
isWarningCleared  : bool
```

### PatchPanelSaveData (`full_output.txt` L1296-1303)
```
patchPanelID      : String        ← UNIQUE ID
position          : Vector3
rotation          : Quaternion
rackPositionUID   : int
patchPanelType    : int
```

### CableSaveData (`full_output.txt` L547-555)
```
cableID           : int           ← UNIQUE ID (incrementing)
startPoint        : CableEndpointSaveData
endPoint          : CableEndpointSaveData
waypoints         : List<Vector3>
midPointPositions : List<Vector3>
maxSpeed          : float
cableColor        : Color
```

### CableEndpointSaveData (`full_output.txt` L538-545)
```
type              : TypeOfLink    ← enum (Server, Switch, PatchPanel, CustomerBase, ...)
position          : Vector3
customerID        : int
switchID          : String
serverID          : String
```

### SFPSaveData (`full_output.txt` L1830-1836)
```
prefabID          : int
position          : Vector3
rotation          : Quaternion
isInserted        : bool
portPosition      : Vector3
```

### ObjectInHand enum (from `item_type_name` in `event.rs`)
```
0 = None
1 = Server1U
2 = Server7U
3 = Server3U
4 = Switch
5 = Rack
6 = CableSpinner
7 = PatchPanel
8 = SFPModule
9 = SFPBox
```

### Key game classes for hooking
```
NetworkMap           (L1146-1195) — RegisterServer, RegisterSwitch, GenerateDeviceName, etc.
Server               (L1664-1717) — ServerInsertedInRack, PowerButton, SetIP, UpdateCustomer, etc.
NetworkSwitch        (L1200-1245) — similar to Server
PatchPanel           (L1281-1300) — similar
RackPosition         (L207)       — InsertItemInRack (coroutine)
RackMount            (L204)       — InstallRack (coroutine)
Rack                 (L196)       — UnmountRack (coroutine)
CableLink            (L36)        — cable endpoint interaction
SFPModule            (L246)       — SlideIntoPort (coroutine)
PlayerManager        (L1445-1475) — objectInHand, numberOfObjectsInHand
SaveSystem           (L1628-1652) — SaveGame, LoadGame
```

---

## Appendix B: Latency Analysis

### Network path (via relay)

```
Client → Relay:  ~75ms  (half of client's ping to relay)
Relay  → Host:   ~75ms  (half of host's ping to relay)
Host   → Relay:  ~75ms
Relay  → Client: ~75ms
─────────────────────────
Total round-trip: ~300ms (with 150ms ping to relay for both parties)
```

### What the player experiences

| Scenario | Latency for actor | Latency for observers |
|----------|------------------|-----------------------|
| Client performs action | **0ms** (optimistic local) | ~300ms (broadcast via host) |
| Host performs action | **0ms** (local) | ~150ms (direct broadcast) |
| ACK arrives at client | ~300ms | N/A |
| Hash-check correction | ~20s + ~300ms | ~20s + ~300ms |

### Why optimistic-local is essential

Without it, the acting player would wait 300ms before seeing their own action — unacceptable for placing objects, connecting cables, etc. With optimistic-local, the acting player sees 0ms delay, and the 300ms only affects remote observers (which is fine for a datacenter simulator).