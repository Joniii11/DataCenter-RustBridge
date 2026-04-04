use bincode::{Decode, Encode};

/// Messages sent over the network between players.
#[derive(Encode, Decode, Debug, Clone)]
pub enum Message {
    /// Player position update (sent frequently, unreliable)
    Position {
        x: f32,
        y: f32,
        z: f32,
        rot_y: f32,
    },

    /// Initial handshake when connecting
    Hello {
        player_name: String,
        mod_version: String,
    },

    /// Response to Hello
    Welcome {
        player_name: String,
        is_host: bool,
        spawn_x: f32,
        spawn_y: f32,
        spawn_z: f32,
    },

    /// Player is disconnecting gracefully
    Goodbye,

    /// Simple ping/pong for connection health
    Ping(u64),
    Pong(u64),

    RequestSave,
    SaveOffer {
        total_bytes: u32,
        chunk_count: u32,
        save_hash: u64,
    },
    SaveChunk {
        index: u32,
        data: Vec<u8>,
    },
    /// Client tells host "I already have this save, stop sending chunks"
    SaveSkip,

    /// Player visual state update (carry, crouch, sit)
    PlayerState {
        object_in_hand: u8,
        num_objects: u8,
        is_crouching: bool,
        is_sitting: bool,
    },
}

const BINCODE_CONFIG: bincode::config::Configuration = bincode::config::standard();

impl Message {
    #[allow(dead_code)]
    pub fn serialize(&self) -> Option<Vec<u8>> {
        bincode::encode_to_vec(self, BINCODE_CONFIG).ok()
    }

    #[allow(dead_code)]
    pub fn deserialize(data: &[u8]) -> Option<Self> {
        bincode::decode_from_slice(data, BINCODE_CONFIG)
            .ok()
            .map(|(msg, _)| msg)
    }

    #[allow(dead_code)]
    pub fn is_reliable(&self) -> bool {
        !matches!(self, Message::Position { .. })
    }
}

/// A message envelope with a target steam ID for targeted delivery
#[derive(Encode, Decode, Debug, Clone)]
pub struct Envelope {
    pub target: u64,
    pub message: Message,
}

impl Envelope {
    /// Create a broadcast envelope
    pub fn broadcast(message: Message) -> Self {
        Self { target: 0, message }
    }

    /// Create a targeted envelope
    pub fn targeted(target: u64, message: Message) -> Self {
        Self { target, message }
    }

    pub fn serialize(&self) -> Option<Vec<u8>> {
        bincode::encode_to_vec(self, BINCODE_CONFIG).ok()
    }

    pub fn deserialize(data: &[u8]) -> Option<Self> {
        bincode::decode_from_slice(data, BINCODE_CONFIG)
            .ok()
            .map(|(env, _)| env)
    }
}
