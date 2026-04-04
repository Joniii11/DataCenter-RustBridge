use bincode::{Decode, Encode};

/// Object type constants matching the games `ObjectInHand` enum
#[allow(dead_code)]
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

/// Compact hash representation of a world state
#[derive(Encode, Decode, Debug, Clone)]
pub struct ObjectHash {
    pub object_id: String,
    pub object_type: u8,
    pub hash: u32,
}

/// object ´has for c# and rust
#[allow(dead_code)]
#[repr(C)]
pub struct ObjectHashFFI {
    pub object_id: [u8; 64],
    pub object_id_len: u32,
    pub object_type: u8,
    pub hash: u32,
}

/// Actions that modify world object state
#[derive(Encode, Decode, Debug, Clone)]
pub enum WorldAction {
    ObjectPickedUp {
        object_id: String,
        object_type: u8,
    },
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
    InstalledInRack {
        object_id: String,
        object_type: u8,
        rack_position_uid: i32,
    },
    RemovedFromRack {
        object_id: String,
        object_type: u8,
    },
    PowerToggled {
        object_id: String,
        is_on: bool,
    },
    PropertyChanged {
        object_id: String,
        key: String,
        value: String,
    },
    CableConnected {
        cable_id: i32,
        start_type: u8,
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
    CableDisconnected {
        cable_id: i32,
    },
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
    ObjectDestroyed {
        object_id: String,
        object_type: u8,
    },
}

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

    /// action performed
    WorldActionMsg {
        seq: u32,
        action: WorldAction,
    },

    /// ack to the action or reject to rollback
    WorldActionAck {
        seq: u32,
        accepted: bool,
    },

    /// broadcast action to all clients
    WorldActionBroadcast {
        action: WorldAction,
    },

    /// sync hash list to prevent desync
    WorldHashCheck {
        hashes: Vec<ObjectHash>,
    },

    /// request sync
    WorldResyncRequest {
        object_id: String,
    },

    /// sync ack
    WorldResyncResponse {
        object_id: String,
        object_type: u8,
        data: Vec<u8>,
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_world_action_roundtrip() {
        let action = WorldAction::InstalledInRack {
            object_id: "SRV_001".to_string(),
            object_type: object_types::SERVER_1U,
            rack_position_uid: 42,
        };

        let msg = Message::WorldActionMsg {
            seq: 1,
            action: action.clone(),
        };

        let bytes = msg.serialize().expect("serialize WorldActionMsg");
        let decoded = Message::deserialize(&bytes).expect("deserialize WorldActionMsg");

        match decoded {
            Message::WorldActionMsg { seq, action } => {
                assert_eq!(seq, 1);
                match action {
                    WorldAction::InstalledInRack {
                        object_id,
                        object_type,
                        rack_position_uid,
                    } => {
                        assert_eq!(object_id, "SRV_001");
                        assert_eq!(object_type, object_types::SERVER_1U);
                        assert_eq!(rack_position_uid, 42);
                    }
                    _ => panic!("wrong action variant"),
                }
            }
            _ => panic!("wrong message variant"),
        }
    }

    #[test]
    fn test_world_action_ack_roundtrip() {
        let msg = Message::WorldActionAck {
            seq: 99,
            accepted: true,
        };
        let bytes = msg.serialize().unwrap();
        let decoded = Message::deserialize(&bytes).unwrap();
        match decoded {
            Message::WorldActionAck { seq, accepted } => {
                assert_eq!(seq, 99);
                assert!(accepted);
            }
            _ => panic!("wrong variant"),
        }
    }

    #[test]
    fn test_world_action_broadcast_roundtrip() {
        let msg = Message::WorldActionBroadcast {
            action: WorldAction::PowerToggled {
                object_id: "SW_42".to_string(),
                is_on: true,
            },
        };
        let bytes = msg.serialize().unwrap();
        let decoded = Message::deserialize(&bytes).unwrap();
        match decoded {
            Message::WorldActionBroadcast { action } => match action {
                WorldAction::PowerToggled { object_id, is_on } => {
                    assert_eq!(object_id, "SW_42");
                    assert!(is_on);
                }
                _ => panic!("wrong action"),
            },
            _ => panic!("wrong variant"),
        }
    }

    #[test]
    fn test_cable_connected_roundtrip() {
        let msg = Message::WorldActionBroadcast {
            action: WorldAction::CableConnected {
                cable_id: 7,
                start_type: 1,
                start_pos_x: 1.0,
                start_pos_y: 2.0,
                start_pos_z: 3.0,
                start_device_id: "SRV_A".to_string(),
                end_type: 2,
                end_pos_x: 4.0,
                end_pos_y: 5.0,
                end_pos_z: 6.0,
                end_device_id: "SW_B".to_string(),
            },
        };
        let bytes = msg.serialize().unwrap();
        let decoded = Message::deserialize(&bytes).unwrap();
        match decoded {
            Message::WorldActionBroadcast { action } => match action {
                WorldAction::CableConnected {
                    cable_id,
                    start_type,
                    start_device_id,
                    end_device_id,
                    ..
                } => {
                    assert_eq!(cable_id, 7);
                    assert_eq!(start_type, 1);
                    assert_eq!(start_device_id, "SRV_A");
                    assert_eq!(end_device_id, "SW_B");
                }
                _ => panic!("wrong action"),
            },
            _ => panic!("wrong variant"),
        }
    }

    #[test]
    fn test_hash_check_roundtrip() {
        let msg = Message::WorldHashCheck {
            hashes: vec![
                ObjectHash {
                    object_id: "SRV_1".to_string(),
                    object_type: 1,
                    hash: 0xDEAD,
                },
                ObjectHash {
                    object_id: "SW_2".to_string(),
                    object_type: 4,
                    hash: 0xBEEF,
                },
            ],
        };
        let bytes = msg.serialize().unwrap();
        let decoded = Message::deserialize(&bytes).unwrap();
        match decoded {
            Message::WorldHashCheck { hashes } => {
                assert_eq!(hashes.len(), 2);
                assert_eq!(hashes[0].object_id, "SRV_1");
                assert_eq!(hashes[0].hash, 0xDEAD);
                assert_eq!(hashes[1].object_id, "SW_2");
                assert_eq!(hashes[1].hash, 0xBEEF);
            }
            _ => panic!("wrong variant"),
        }
    }

    #[test]
    fn test_resync_roundtrip() {
        let msg = Message::WorldResyncResponse {
            object_id: "SRV_X".to_string(),
            object_type: 1,
            data: vec![1, 2, 3, 4, 5],
        };
        let bytes = msg.serialize().unwrap();
        let decoded = Message::deserialize(&bytes).unwrap();
        match decoded {
            Message::WorldResyncResponse {
                object_id,
                object_type,
                data,
            } => {
                assert_eq!(object_id, "SRV_X");
                assert_eq!(object_type, 1);
                assert_eq!(data, vec![1, 2, 3, 4, 5]);
            }
            _ => panic!("wrong variant"),
        }
    }

    #[test]
    fn test_all_world_action_variants_serialize() {
        // Ensure every variant can round-trip without panicking
        let actions = vec![
            WorldAction::ObjectPickedUp {
                object_id: "a".into(),
                object_type: 1,
            },
            WorldAction::ObjectDropped {
                object_id: "b".into(),
                object_type: 2,
                pos_x: 1.0,
                pos_y: 2.0,
                pos_z: 3.0,
                rot_x: 0.0,
                rot_y: 0.0,
                rot_z: 0.0,
                rot_w: 1.0,
            },
            WorldAction::InstalledInRack {
                object_id: "c".into(),
                object_type: 1,
                rack_position_uid: 5,
            },
            WorldAction::RemovedFromRack {
                object_id: "d".into(),
                object_type: 4,
            },
            WorldAction::PowerToggled {
                object_id: "e".into(),
                is_on: false,
            },
            WorldAction::PropertyChanged {
                object_id: "f".into(),
                key: "ip".into(),
                value: "10.0.0.1".into(),
            },
            WorldAction::CableConnected {
                cable_id: 1,
                start_type: 0,
                start_pos_x: 0.0,
                start_pos_y: 0.0,
                start_pos_z: 0.0,
                start_device_id: "g".into(),
                end_type: 0,
                end_pos_x: 0.0,
                end_pos_y: 0.0,
                end_pos_z: 0.0,
                end_device_id: "h".into(),
            },
            WorldAction::CableDisconnected { cable_id: 2 },
            WorldAction::ObjectSpawned {
                object_id: "i".into(),
                object_type: 5,
                prefab_id: 10,
                pos_x: 0.0,
                pos_y: 0.0,
                pos_z: 0.0,
                rot_x: 0.0,
                rot_y: 0.0,
                rot_z: 0.0,
                rot_w: 1.0,
            },
            WorldAction::ObjectDestroyed {
                object_id: "j".into(),
                object_type: 8,
            },
        ];

        for (i, action) in actions.into_iter().enumerate() {
            let msg = Message::WorldActionMsg {
                seq: i as u32,
                action,
            };
            let bytes = msg
                .serialize()
                .unwrap_or_else(|| panic!("failed to serialize variant {}", i));
            let _decoded = Message::deserialize(&bytes)
                .unwrap_or_else(|| panic!("failed to deserialize variant {}", i));
        }
    }
}
