//! World object synchronization state tracking.
//!
//! Tracks pending actions (optimistic-local client model), sequence numbers,
//! timeouts, and hash-check state for desync detection.
#![allow(dead_code)]

use std::collections::HashMap;

use crate::protocol::WorldAction;

pub const WORLD_ACTION_TIMEOUT_SECS: f32 = 5.0;
pub const HASH_CHECK_INTERVAL_SECS: f32 = 20.0;

/// An action the local client performed optimistically awaiting host ACK.
#[derive(Debug)]
pub struct PendingAction {
    pub seq: u32,
    pub action: WorldAction,
    pub sent_at: f32,
    pub rollback_info: RollbackInfo,
}

/// Data needed to undo an optimistically applied action if the host rejects it or timeout
#[derive(Debug, Clone)]
pub enum RollbackInfo {
    UndoPickup {
        object_id: String,
        object_type: u8,
        original_pos: (f32, f32, f32),
        original_rot: (f32, f32, f32, f32),
    },
    UndoDrop {
        object_id: String,
    },
    UndoInstall {
        object_id: String,
        object_type: u8,
        previous_pos: (f32, f32, f32),
        previous_rot: (f32, f32, f32, f32),
    },
    UndoRemoveFromRack {
        object_id: String,
        object_type: u8,
        rack_position_uid: i32,
    },
    UndoPowerToggle {
        object_id: String,
        was_on: bool,
    },
    UndoPropertyChange {
        object_id: String,
        key: String,
        old_value: String,
    },
    UndoCableConnect {
        cable_id: i32,
    },
    UndoCableDisconnect {
        cable_id: i32,
        start_type: u8,
        start_pos: (f32, f32, f32),
        start_device_id: String,
        end_type: u8,
        end_pos: (f32, f32, f32),
        end_device_id: String,
    },
    UndoSpawn {
        object_id: String,
    },
    UndoDestroy {
        object_id: String,
        object_type: u8,
        prefab_id: i32,
        pos: (f32, f32, f32),
        rot: (f32, f32, f32, f32),
    },
    None,
}

/// Tracks all world synchronization state for the local player
pub struct WorldSyncState {
    pub next_seq: u32,
    pub pending_actions: Vec<PendingAction>,
    pub hash_check_timer: f32,
    pub last_known_hashes: HashMap<String, u32>,
    pub game_time: f32,
}

impl WorldSyncState {
    /// Create a new empty world sync state
    pub fn new() -> Self {
        Self {
            next_seq: 1,
            pending_actions: Vec::new(),
            hash_check_timer: 0.0,
            last_known_hashes: HashMap::new(),
            game_time: 0.0,
        }
    }

    /// Allocate the next sequence number for an outgoing action
    pub fn next_seq(&mut self) -> u32 {
        let seq = self.next_seq;
        self.next_seq = self.next_seq.wrapping_add(1);
        if self.next_seq == 0 {
            self.next_seq = 1;
        }
        seq
    }

    /// Register a pending action (client only)
    pub fn register_pending(&mut self, seq: u32, action: WorldAction, rollback_info: RollbackInfo) {
        self.pending_actions.push(PendingAction {
            seq,
            action,
            sent_at: self.game_time,
            rollback_info,
        });
    }

    /// Remove and return a pending action by sequence number
    pub fn remove_pending(&mut self, seq: u32) -> Option<PendingAction> {
        if let Some(idx) = self.pending_actions.iter().position(|p| p.seq == seq) {
            Some(self.pending_actions.remove(idx))
        } else {
            None
        }
    }

    /// Find all pending actions that have timed out
    pub fn drain_timed_out(&mut self) -> Vec<PendingAction> {
        let timeout = WORLD_ACTION_TIMEOUT_SECS;
        let now = self.game_time;
        let mut timed_out = Vec::new();
        self.pending_actions.retain(|p| {
            if now - p.sent_at >= timeout {
                timed_out.push(PendingAction {
                    seq: p.seq,
                    action: p.action.clone(),
                    sent_at: p.sent_at,
                    rollback_info: p.rollback_info.clone(),
                });
                false // remove from pending
            } else {
                true // keep
            }
        });
        timed_out
    }

    /// Check if there a pending action for a given object ID
    pub fn has_pending_for_object(&self, object_id: &str) -> bool {
        self.pending_actions.iter().any(|p| match &p.action {
            WorldAction::ObjectPickedUp { object_id: id, .. }
            | WorldAction::ObjectDropped { object_id: id, .. }
            | WorldAction::InstalledInRack { object_id: id, .. }
            | WorldAction::RemovedFromRack { object_id: id, .. }
            | WorldAction::PowerToggled { object_id: id, .. }
            | WorldAction::PropertyChanged { object_id: id, .. }
            | WorldAction::ObjectSpawned { object_id: id, .. }
            | WorldAction::ObjectDestroyed { object_id: id, .. } => id == object_id,
            WorldAction::CableConnected { .. } | WorldAction::CableDisconnected { .. } => false,
        })
    }

    /// Reset all state
    pub fn reset(&mut self) {
        self.next_seq = 1;
        self.pending_actions.clear();
        self.hash_check_timer = 0.0;
        self.last_known_hashes.clear();
        self.game_time = 0.0;
    }
}

impl Default for WorldSyncState {
    fn default() -> Self {
        Self::new()
    }
}
