use std::collections::HashMap;
use std::time::Instant;

/// Data shared with C# for rendering remote players
#[repr(C)]
#[derive(Clone)]
pub struct RemotePlayerData {
    pub steam_id: u64,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub rot_y: f32,
    pub name: [u8; 64],
    pub connected: u8,
}

impl Default for RemotePlayerData {
    fn default() -> Self {
        Self {
            steam_id: 0,
            x: 0.0,
            y: 0.0,
            z: 0.0,
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
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub rot_y: f32,
    pub last_update: Instant,
}

impl RemotePlayer {
    pub fn new(steam_id: u64, name: String) -> Self {
        Self {
            steam_id,
            name,
            x: 0.0,
            y: 0.0,
            z: 0.0,
            rot_y: 0.0,
            last_update: Instant::now(),
        }
    }

    pub fn update_position(&mut self, x: f32, y: f32, z: f32, rot_y: f32) {
        self.x = x;
        self.y = y;
        self.z = z;
        self.rot_y = rot_y;
        self.last_update = Instant::now();
    }

    pub fn is_stale(&self) -> bool {
        self.last_update.elapsed().as_secs() > 10
    }

    pub fn to_ffi(&self) -> RemotePlayerData {
        let mut data = RemotePlayerData {
            steam_id: self.steam_id,
            x: self.x,
            y: self.y,
            z: self.z,
            rot_y: self.rot_y,
            name: [0u8; 64],
            connected: 1,
        };
        data.set_name(&self.name);
        data
    }
}

/// Tracks all connected remote players
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
        self.players
            .insert(steam_id, RemotePlayer::new(steam_id, name));
    }

    pub fn remove_player(&mut self, steam_id: u64) {
        self.players.remove(&steam_id);
    }

    pub fn update_position(&mut self, steam_id: u64, x: f32, y: f32, z: f32, rot_y: f32) {
        if let Some(player) = self.players.get_mut(&steam_id) {
            player.update_position(x, y, z, rot_y);
        }
    }

    pub fn has_player(&self, steam_id: u64) -> bool {
        self.players.contains_key(&steam_id)
    }

    /// Remove players that haven't sent data in a while
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

    /// Fill a C-compatible buffer with player data for rendering
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
}
