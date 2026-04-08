use dc_api::Vec3;

use crate::net;
use crate::player::{PlayerStateSnapshot, PlayerTracker};
use crate::world::WorldSyncState;
use std::collections::HashMap;
use std::ffi::CString;
use std::sync::Mutex;
use std::sync::OnceLock;

pub const POSITION_SEND_INTERVAL: f32 = 0.05;
pub const HELLO_RETRY_INTERVAL: f32 = 2.0;
pub const HELLO_MAX_RETRIES: u32 = 15;
pub const DEFAULT_RELAY_URL: &str = "ws://192.99.16.77:9943"; // FIXME: Proper URL before release
pub const SAVE_CHUNK_SIZE: usize = 60_000;
pub const PLAYER_STATE_HEARTBEAT_INTERVAL: f32 = 1.0;

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

/// Per-client save transfer progress.
pub struct SaveTransferState {
    pub send_index: u32,
}

pub struct SessionState {
    pub peer_id: u64,
    pub is_host: bool,
    pub my_id: u64,
    pub connected: bool,
    pub connecting: bool,
    pub relay: Option<net::RelayConnection>,
    pub room_code: Option<String>,
    pub room_code_cstr: Option<CString>,
    pub join_ok_received: bool,
    pub hello_retry_timer: f32,
    pub hello_retry_count: u32,
    pub join_state: JoinState,
    pub default_spawn: Option<Vec3>,
    pub pos_timer: f32,
    pub last_sent_player_state: PlayerStateSnapshot,
    pub player_state_heartbeat_timer: f32,
    pub rack_uids_ensured: bool,
}

impl SessionState {
    fn new() -> Self {
        Self {
            peer_id: 0,
            is_host: false,
            my_id: 0,
            connected: false,
            connecting: false,
            relay: None,
            room_code: None,
            room_code_cstr: None,
            join_ok_received: false,
            hello_retry_timer: 0.0,
            hello_retry_count: 0,
            join_state: JoinState::Idle,
            default_spawn: None,
            pos_timer: 0.0,
            last_sent_player_state: PlayerStateSnapshot::default(),
            player_state_heartbeat_timer: 0.0,
            rack_uids_ensured: false,
        }
    }
}

pub struct SaveState {
    pub outgoing: Option<Vec<u8>>,
    pub chunk_count: u32,
    pub transfers: HashMap<u64, SaveTransferState>,
    pub incoming_total: u32,
    pub incoming_chunk_count: u32,
    pub incoming_data: Vec<u8>,
    pub incoming_received: Vec<bool>,
    pub data_ready: Option<Vec<u8>>,
    pub loaded: bool,
    pub skip_next_request: bool,
    pub local_hash: u64,
    pub up_to_date: bool,
}

impl SaveState {
    fn new() -> Self {
        Self {
            outgoing: None,
            chunk_count: 0,
            transfers: HashMap::new(),
            incoming_total: 0,
            incoming_chunk_count: 0,
            incoming_data: Vec::new(),
            incoming_received: Vec::new(),
            data_ready: None,
            loaded: false,
            skip_next_request: false,
            local_hash: 0,
            up_to_date: false,
        }
    }
}

pub struct CarryState {
    pub prev_count: u8,
    pub held_id: String,
    pub held_type: u8,
    pub suppress_next_drop: bool,
    pub last_install_id: String,
    pub last_install_time: f32,
}

impl CarryState {
    fn new() -> Self {
        Self {
            prev_count: 0,
            held_id: String::new(),
            held_type: 0,
            suppress_next_drop: false,
            last_install_id: String::new(),
            last_install_time: 0.0,
        }
    }
}

pub struct MultiplayerState {
    pub tracker: PlayerTracker,
    pub session: SessionState,
    pub save: SaveState,
    pub carry: CarryState,
    pub world_sync: WorldSyncState,
    pub executing_remote_action: bool,
}

impl MultiplayerState {
    fn new() -> Self {
        Self {
            tracker: PlayerTracker::new(),
            session: SessionState::new(),
            save: SaveState::new(),
            carry: CarryState::new(),
            world_sync: WorldSyncState::new(),
            executing_remote_action: false,
        }
    }
}

static STATE: OnceLock<Mutex<MultiplayerState>> = OnceLock::new();

pub fn init_state() {
    let _ = STATE.set(Mutex::new(MultiplayerState::new()));
}

pub fn with_state<F, R>(f: F) -> Option<R>
where
    F: FnOnce(&mut MultiplayerState) -> R,
{
    STATE
        .get()
        .and_then(|m| m.lock().ok())
        .map(|mut s| f(&mut s))
}

pub fn compute_save_hash(data: &[u8]) -> u64 {
    use std::hash::{Hash, Hasher};
    let mut hasher = std::collections::hash_map::DefaultHasher::new();
    data.hash(&mut hasher);
    hasher.finish()
}
