//! Shared multiplayer state, constants, and join-state machine.

use dc_api::Vec3;

use crate::net;
use crate::player::{PlayerStateSnapshot, PlayerTracker};
use std::collections::HashMap;
use std::ffi::CString;
use std::sync::Mutex;
use std::sync::OnceLock;

// ── Constants ────────────────────────────────────────────────────────────────

pub const POSITION_SEND_INTERVAL: f32 = 0.05;
pub const HELLO_RETRY_INTERVAL: f32 = 2.0;
pub const HELLO_MAX_RETRIES: u32 = 15;
pub const DEFAULT_RELAY_URL: &str = "ws://192.99.16.77:9943"; // FIXME: Proper URL before release
pub const SAVE_CHUNK_SIZE: usize = 60_000;
pub const PLAYER_STATE_HEARTBEAT_INTERVAL: f32 = 1.0;

/// Join state — Rust is the authority, C# polls/sets via FFI.
#[repr(u32)]
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum JoinState {
    Idle = 0,
    WaitingForSave = 1,
    SaveReady = 2,
    SaveUpToDate = 3,
    LoadingScene = 4,
    Loaded = 5,
}

impl JoinState {
    pub fn from_u32(val: u32) -> Self {
        match val {
            1 => Self::WaitingForSave,
            2 => Self::SaveReady,
            3 => Self::SaveUpToDate,
            4 => Self::LoadingScene,
            5 => Self::Loaded,
            _ => Self::Idle,
        }
    }
}

/// per client save transfer state
pub struct SaveTransferState {
    pub send_index: u32,
}

static STATE: OnceLock<Mutex<MultiplayerState>> = OnceLock::new();

pub struct MultiplayerState {
    pub tracker: PlayerTracker,
    pub peer_id: u64,
    pub is_host: bool,
    pub my_id: u64,
    pub pos_timer: f32,
    pub connected: bool,
    pub connecting: bool,
    pub hello_retry_timer: f32,
    pub hello_retry_count: u32,
    pub relay: Option<net::RelayConnection>,
    pub room_code: Option<String>,
    pub room_code_cstr: Option<CString>,
    pub join_ok_received: bool,

    pub save_outgoing: Option<Vec<u8>>,
    pub save_chunk_count: u32,
    pub save_transfers: HashMap<u64, SaveTransferState>,

    pub save_incoming_total: u32,
    pub save_incoming_chunk_count: u32,
    pub save_incoming_data: Vec<u8>,
    pub save_incoming_received: Vec<bool>,
    pub save_data_ready: Option<Vec<u8>>,
    pub save_loaded: bool,
    pub skip_next_save_request: bool,

    pub local_save_hash: u64,
    pub save_up_to_date: bool,

    pub join_state: JoinState,

    pub last_sent_player_state: PlayerStateSnapshot,
    pub player_state_heartbeat_timer: f32,

    pub default_spawn: Option<Vec3>,
}

impl MultiplayerState {
    fn new() -> Self {
        Self {
            tracker: PlayerTracker::new(),
            peer_id: 0,
            is_host: false,
            my_id: 0,
            pos_timer: 0.0,
            connected: false,
            connecting: false,
            hello_retry_timer: 0.0,
            hello_retry_count: 0,
            relay: None,
            room_code: None,
            room_code_cstr: None,
            join_ok_received: false,

            save_outgoing: None,
            save_chunk_count: 0,
            save_transfers: HashMap::new(),

            save_incoming_total: 0,
            save_incoming_chunk_count: 0,
            save_incoming_data: Vec::new(),
            save_incoming_received: Vec::new(),
            save_data_ready: None,
            save_loaded: false,
            skip_next_save_request: false,

            local_save_hash: 0,
            save_up_to_date: false,

            join_state: JoinState::Idle,

            last_sent_player_state: PlayerStateSnapshot::default(),
            player_state_heartbeat_timer: 0.0,

            default_spawn: None,
        }
    }
}

/// Initialize the global state. Call once from mod_entry.
pub fn init_state() {
    let _ = STATE.set(Mutex::new(MultiplayerState::new()));
}

/// Access the global multiplayer state under a lock.
pub fn with_state<F, R>(f: F) -> Option<R>
where
    F: FnOnce(&mut MultiplayerState) -> R,
{
    STATE
        .get()
        .and_then(|m| m.lock().ok())
        .map(|mut s| f(&mut s))
}

/// Compute a hash of save data for versioning.
pub fn compute_save_hash(data: &[u8]) -> u64 {
    use std::hash::{Hash, Hasher};
    let mut hasher = std::collections::hash_map::DefaultHasher::new();
    data.hash(&mut hasher);
    hasher.finish()
}
