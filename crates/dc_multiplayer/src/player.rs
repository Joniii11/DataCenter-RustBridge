use std::collections::HashMap;
use std::time::Instant;

use dc_api::Vec3;

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct PlayerStateSnapshot {
    pub object_in_hand: u8,
    pub num_objects: u8,
    pub is_crouching: bool,
    pub is_sitting: bool,
}

fn lerp_angle(a: f32, b: f32, t: f32) -> f32 {
    let mut delta = (b - a) % 360.0;
    if delta > 180.0 {
        delta -= 360.0;
    }
    if delta < -180.0 {
        delta += 360.0;
    }
    a + delta * t
}

/// Shared with C# via FFI.
#[repr(C)]
#[derive(Clone)]
pub struct RemotePlayerData {
    pub steam_id: u64,
    pub pos: Vec3,
    pub rot_y: f32,
    pub name: [u8; 64],
    pub connected: u8,
}

impl Default for RemotePlayerData {
    fn default() -> Self {
        Self {
            steam_id: 0,
            pos: Vec3::zero(),
            rot_y: 0.0,
            name: [0u8; 64],
            connected: 0,
        }
    }
}

impl RemotePlayerData {
    pub fn set_name(&mut self, name: &str) {
        let bytes = name.as_bytes();
        let len = bytes.len().min(63);
        self.name[..len].copy_from_slice(&bytes[..len]);
        self.name[len] = 0;
    }
}

pub struct RemotePlayer {
    pub steam_id: u64,
    pub name: String,
    pub pos: Vec3,
    pub rot_y: f32,
    pub last_update: Instant,

    pub entity_id: Option<u32>,

    pub prev_pos: Vec3,
    pub prev_rot_y: f32,

    pub network_update_time: Instant,
    pub player_state: PlayerStateSnapshot,
    pub last_applied_carry_type: u8,
    pub use_default_spawn: bool,
    pub spawn_time: Option<Instant>,
    pub uma_ready_time: Option<Instant>,
    pub collider_added: bool,
}

impl RemotePlayer {
    pub fn new(steam_id: u64, name: String) -> Self {
        Self {
            steam_id,
            name,
            pos: Vec3::zero(),
            rot_y: 0.0,
            last_update: Instant::now(),
            entity_id: None,
            prev_pos: Vec3::zero(),
            prev_rot_y: 0.0,
            network_update_time: Instant::now(),
            player_state: PlayerStateSnapshot::default(),
            last_applied_carry_type: 0,
            use_default_spawn: true,
            spawn_time: None,
            uma_ready_time: None,
            collider_added: false,
        }
    }

    pub fn update_position(&mut self, pos: Vec3, rot_y: f32) {
        self.prev_pos = self.pos;
        self.prev_rot_y = self.rot_y;
        self.pos = pos;
        self.rot_y = rot_y;
        self.network_update_time = Instant::now();
        self.last_update = Instant::now();
    }

    pub fn interpolated_position(&self) -> (f32, f32, f32, f32) {
        let elapsed = self.network_update_time.elapsed().as_secs_f32();
        let t = (elapsed / 0.05).clamp(0.0, 1.5);

        let ix = self.prev_pos.x + (self.pos.x - self.prev_pos.x) * t;
        let iy = self.prev_pos.y + (self.pos.y - self.prev_pos.y) * t;
        let iz = self.prev_pos.z + (self.pos.z - self.prev_pos.z) * t;

        let t_rot = t.min(1.0);
        let irot = lerp_angle(self.prev_rot_y, self.rot_y, t_rot);
        (ix, iy, iz, irot)
    }

    /// True when we have a valid position but no entity spawned yet.
    pub fn needs_spawn(&self) -> bool {
        self.entity_id.is_none() && (self.pos.x != 0.0 || self.pos.y != 0.0 || self.pos.z != 0.0)
    }

    pub fn is_stale(&self) -> bool {
        self.last_update.elapsed().as_secs() > 10
    }

    pub fn to_ffi(&self) -> RemotePlayerData {
        let mut data = RemotePlayerData {
            steam_id: self.steam_id,
            pos: self.pos,
            rot_y: self.rot_y,
            name: [0u8; 64],
            connected: 1,
        };
        data.set_name(&self.name);
        data
    }
}

pub struct PlayerTracker {
    players: HashMap<u64, RemotePlayer>,
}

impl PlayerTracker {
    pub fn new() -> Self {
        Self {
            players: HashMap::new(),
        }
    }

    pub fn add_player(&mut self, steam_id: u64, name: String) {
        if let Some(existing) = self.players.get_mut(&steam_id) {
            existing.name = name;
            existing.last_update = Instant::now();
        } else {
            self.players
                .insert(steam_id, RemotePlayer::new(steam_id, name));
        }
    }

    #[allow(dead_code)]
    pub fn remove_player(&mut self, steam_id: u64) {
        self.players.remove(&steam_id);
    }

    pub fn update_position(&mut self, steam_id: u64, pos: Vec3, rot_y: f32) {
        if let Some(player) = self.players.get_mut(&steam_id) {
            player.update_position(pos, rot_y);
        }
    }

    pub fn has_player(&self, steam_id: u64) -> bool {
        self.players.contains_key(&steam_id)
    }

    #[allow(dead_code)]
    pub fn cleanup_stale(&mut self) -> Vec<u64> {
        let stale: Vec<u64> = self
            .players
            .iter()
            .filter(|(_, p)| p.is_stale())
            .map(|(id, _)| *id)
            .collect();
        for id in &stale {
            self.players.remove(id);
        }
        stale
    }

    pub fn player_count(&self) -> usize {
        self.players.len()
    }

    pub fn fill_ffi_buffer(&self, buf: &mut [RemotePlayerData]) -> usize {
        let count = self.players.len().min(buf.len());
        for (i, player) in self.players.values().enumerate() {
            if i >= buf.len() {
                break;
            }
            buf[i] = player.to_ffi();
        }
        count
    }

    pub fn set_entity_id(&mut self, steam_id: u64, entity_id: u32) {
        if let Some(player) = self.players.get_mut(&steam_id) {
            player.entity_id = Some(entity_id);
        }
    }

    pub fn remove_player_with_entity(&mut self, steam_id: u64) -> Option<u32> {
        self.players.remove(&steam_id).and_then(|p| p.entity_id)
    }

    pub fn get_player_mut(&mut self, steam_id: u64) -> Option<&mut RemotePlayer> {
        self.players.get_mut(&steam_id)
    }

    pub fn for_each_player<F: FnMut(&RemotePlayer)>(&self, mut f: F) {
        for player in self.players.values() {
            f(player);
        }
    }

    pub fn for_each_player_mut<F: FnMut(&mut RemotePlayer)>(&mut self, mut f: F) {
        for player in self.players.values_mut() {
            f(player);
        }
    }

    pub fn get_all_entity_ids(&self) -> Vec<u32> {
        self.players.values().filter_map(|p| p.entity_id).collect()
    }

    pub fn cleanup_stale_with_entities(&mut self) -> Vec<(u64, Option<u32>)> {
        let stale: Vec<(u64, Option<u32>)> = self
            .players
            .iter()
            .filter(|(_, p)| p.is_stale())
            .map(|(id, p)| (*id, p.entity_id))
            .collect();
        for (id, _) in &stale {
            self.players.remove(id);
        }
        stale
    }
}
