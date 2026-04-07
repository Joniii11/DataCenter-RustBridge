use std::collections::HashMap;

use crate::protocol::WorldAction;

use super::RollbackInfo;

pub const WORLD_ACTION_TIMEOUT_SECS: f32 = 5.0;
pub const HASH_CHECK_INTERVAL_SECS: f32 = 20.0;

pub struct PendingAction {
    pub seq: u32,
    pub action: WorldAction,
    pub sent_at: f32,
    pub rollback_info: RollbackInfo,
}

pub struct WorldSyncState {
    pub next_seq: u32,
    pub pending_actions: Vec<PendingAction>,
    pub hash_check_timer: f32,
    pub last_known_hashes: HashMap<String, u32>,
    pub game_time: f32,
}

impl WorldSyncState {
    pub fn new() -> Self {
        Self {
            next_seq: 1,
            pending_actions: Vec::new(),
            hash_check_timer: 0.0,
            last_known_hashes: HashMap::new(),
            game_time: 0.0,
        }
    }

    pub fn next_seq(&mut self) -> u32 {
        let seq = self.next_seq;
        self.next_seq = self.next_seq.wrapping_add(1);
        if self.next_seq == 0 {
            self.next_seq = 1;
        }
        seq
    }

    pub fn register_pending(&mut self, seq: u32, action: WorldAction, rollback_info: RollbackInfo) {
        self.pending_actions.push(PendingAction {
            seq,
            action,
            sent_at: self.game_time,
            rollback_info,
        });
    }

    pub fn remove_pending(&mut self, seq: u32) -> Option<PendingAction> {
        if let Some(idx) = self.pending_actions.iter().position(|p| p.seq == seq) {
            Some(self.pending_actions.remove(idx))
        } else {
            None
        }
    }

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
                false
            } else {
                true
            }
        });
        timed_out
    }

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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::WorldAction;

    fn pickup_action(id: &str) -> WorldAction {
        WorldAction::ObjectPickedUp {
            object_id: id.to_string(),
            object_type: 1,
        }
    }

    fn power_action(id: &str, is_on: bool) -> WorldAction {
        WorldAction::PowerToggled {
            object_id: id.to_string(),
            is_on,
        }
    }

    fn cable_action(cable_id: i32) -> WorldAction {
        WorldAction::CableConnected {
            cable_id,
            start_type: 0,
            start_pos_x: 0.0,
            start_pos_y: 0.0,
            start_pos_z: 0.0,
            start_device_id: "dev_a".into(),
            end_type: 0,
            end_pos_x: 1.0,
            end_pos_y: 1.0,
            end_pos_z: 1.0,
            end_device_id: "dev_b".into(),
        }
    }

    #[test]
    fn test_new_initial_state() {
        let state = WorldSyncState::new();
        assert_eq!(state.next_seq, 1);
        assert!(state.pending_actions.is_empty());
        assert_eq!(state.hash_check_timer, 0.0);
        assert!(state.last_known_hashes.is_empty());
        assert_eq!(state.game_time, 0.0);
    }

    #[test]
    fn test_default_equals_new() {
        let a = WorldSyncState::new();
        let b = WorldSyncState::default();
        assert_eq!(a.next_seq, b.next_seq);
        assert_eq!(a.pending_actions.len(), b.pending_actions.len());
        assert_eq!(a.game_time, b.game_time);
    }

    #[test]
    fn test_next_seq_increments() {
        let mut state = WorldSyncState::new();
        assert_eq!(state.next_seq(), 1);
        assert_eq!(state.next_seq(), 2);
        assert_eq!(state.next_seq(), 3);
    }

    #[test]
    fn test_next_seq_wraps_and_skips_zero() {
        let mut state = WorldSyncState::new();
        state.next_seq = u32::MAX;
        assert_eq!(state.next_seq(), u32::MAX);
        assert_eq!(state.next_seq, 1);
        assert_eq!(state.next_seq(), 1);
        assert_eq!(state.next_seq(), 2);
    }

    #[test]
    fn test_next_seq_wraps_from_max_minus_one() {
        let mut state = WorldSyncState::new();
        state.next_seq = u32::MAX - 1;
        assert_eq!(state.next_seq(), u32::MAX - 1); // returns MAX-1
        assert_eq!(state.next_seq(), u32::MAX); // returns MAX
        assert_eq!(state.next_seq(), 1); // wrapped past 0
    }

    #[test]
    fn test_register_pending_adds_action() {
        let mut state = WorldSyncState::new();
        state.game_time = 10.0;
        state.register_pending(1, pickup_action("srv_1"), RollbackInfo::None);

        assert_eq!(state.pending_actions.len(), 1);
        assert_eq!(state.pending_actions[0].seq, 1);
        assert_eq!(state.pending_actions[0].sent_at, 10.0);
    }

    #[test]
    fn test_register_multiple_pending() {
        let mut state = WorldSyncState::new();
        state.game_time = 1.0;
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);
        state.game_time = 2.0;
        state.register_pending(2, pickup_action("b"), RollbackInfo::None);
        state.game_time = 3.0;
        state.register_pending(3, pickup_action("c"), RollbackInfo::None);

        assert_eq!(state.pending_actions.len(), 3);
        assert_eq!(state.pending_actions[0].seq, 1);
        assert_eq!(state.pending_actions[1].seq, 2);
        assert_eq!(state.pending_actions[2].seq, 3);
    }

    #[test]
    fn test_register_pending_stores_rollback_info() {
        let mut state = WorldSyncState::new();
        let rollback = RollbackInfo::UndoPowerToggle {
            object_id: "srv_1".into(),
            was_on: true,
        };
        state.register_pending(1, power_action("srv_1", false), rollback);

        assert_eq!(state.pending_actions.len(), 1);
        match &state.pending_actions[0].rollback_info {
            RollbackInfo::UndoPowerToggle { object_id, was_on } => {
                assert_eq!(object_id, "srv_1");
                assert!(was_on);
            }
            other => panic!("Expected UndoPowerToggle, got {:?}", other),
        }
    }

    #[test]
    fn test_remove_pending_returns_correct_action() {
        let mut state = WorldSyncState::new();
        state.register_pending(10, pickup_action("a"), RollbackInfo::None);
        state.register_pending(20, pickup_action("b"), RollbackInfo::None);
        state.register_pending(30, pickup_action("c"), RollbackInfo::None);

        let removed = state.remove_pending(20);
        assert!(removed.is_some());
        assert_eq!(removed.unwrap().seq, 20);
        assert_eq!(state.pending_actions.len(), 2);
    }

    #[test]
    fn test_remove_pending_nonexistent_returns_none() {
        let mut state = WorldSyncState::new();
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);

        let removed = state.remove_pending(999);
        assert!(removed.is_none());
        assert_eq!(state.pending_actions.len(), 1); // unchanged
    }

    #[test]
    fn test_remove_pending_preserves_order() {
        let mut state = WorldSyncState::new();
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);
        state.register_pending(2, pickup_action("b"), RollbackInfo::None);
        state.register_pending(3, pickup_action("c"), RollbackInfo::None);

        state.remove_pending(2);
        assert_eq!(state.pending_actions.len(), 2);
        assert_eq!(state.pending_actions[0].seq, 1);
        assert_eq!(state.pending_actions[1].seq, 3);
    }

    #[test]
    fn test_remove_pending_first_match_only() {
        let mut state = WorldSyncState::new();
        state.register_pending(5, pickup_action("a"), RollbackInfo::None);
        state.register_pending(5, pickup_action("b"), RollbackInfo::None);

        let removed = state.remove_pending(5);
        assert!(removed.is_some());
        assert_eq!(state.pending_actions.len(), 1);
    }

    #[test]
    fn test_drain_timed_out_returns_expired() {
        let mut state = WorldSyncState::new();
        state.game_time = 0.0;
        state.register_pending(1, pickup_action("old"), RollbackInfo::None);

        state.game_time = WORLD_ACTION_TIMEOUT_SECS + 1.0;

        let timed_out = state.drain_timed_out();
        assert_eq!(timed_out.len(), 1);
        assert_eq!(timed_out[0].seq, 1);
        assert!(state.pending_actions.is_empty());
    }

    #[test]
    fn test_drain_timed_out_keeps_fresh() {
        let mut state = WorldSyncState::new();
        state.game_time = 0.0;
        state.register_pending(1, pickup_action("old"), RollbackInfo::None);

        state.game_time = 3.0;
        state.register_pending(2, pickup_action("fresh"), RollbackInfo::None);

        state.game_time = WORLD_ACTION_TIMEOUT_SECS + 1.0;

        let timed_out = state.drain_timed_out();
        assert_eq!(timed_out.len(), 1);
        assert_eq!(timed_out[0].seq, 1);
        assert_eq!(state.pending_actions.len(), 1);
        assert_eq!(state.pending_actions[0].seq, 2);
    }

    #[test]
    fn test_drain_timed_out_none_expired() {
        let mut state = WorldSyncState::new();
        state.game_time = 10.0;
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);
        state.register_pending(2, pickup_action("b"), RollbackInfo::None);

        state.game_time = 11.0;

        let timed_out = state.drain_timed_out();
        assert!(timed_out.is_empty());
        assert_eq!(state.pending_actions.len(), 2);
    }

    #[test]
    fn test_drain_timed_out_all_expired() {
        let mut state = WorldSyncState::new();
        state.game_time = 0.0;
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);
        state.game_time = 1.0;
        state.register_pending(2, pickup_action("b"), RollbackInfo::None);
        state.game_time = 2.0;
        state.register_pending(3, pickup_action("c"), RollbackInfo::None);

        state.game_time = 100.0;

        let timed_out = state.drain_timed_out();
        assert_eq!(timed_out.len(), 3);
        assert!(state.pending_actions.is_empty());
    }

    #[test]
    fn test_drain_timed_out_exact_boundary() {
        let mut state = WorldSyncState::new();
        state.game_time = 0.0;
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);

        state.game_time = WORLD_ACTION_TIMEOUT_SECS;

        let timed_out = state.drain_timed_out();
        assert_eq!(
            timed_out.len(),
            1,
            "Action at exact timeout boundary should be drained"
        );
    }

    #[test]
    fn test_drain_timed_out_just_before_boundary() {
        let mut state = WorldSyncState::new();
        state.game_time = 0.0;
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);

        state.game_time = WORLD_ACTION_TIMEOUT_SECS - 0.001;

        let timed_out = state.drain_timed_out();
        assert!(
            timed_out.is_empty(),
            "Action just before timeout should NOT be drained"
        );
        assert_eq!(state.pending_actions.len(), 1);
    }

    #[test]
    fn test_drain_timed_out_preserves_rollback_info() {
        let mut state = WorldSyncState::new();
        state.game_time = 0.0;
        let rollback = RollbackInfo::UndoPickup {
            object_id: "srv_1".into(),
            object_type: 1,
            original_pos: (1.0, 2.0, 3.0),
            original_rot: (0.0, 0.0, 0.0, 1.0),
        };
        state.register_pending(1, pickup_action("srv_1"), rollback);

        state.game_time = WORLD_ACTION_TIMEOUT_SECS + 1.0;
        let timed_out = state.drain_timed_out();
        assert_eq!(timed_out.len(), 1);
        match &timed_out[0].rollback_info {
            RollbackInfo::UndoPickup {
                object_id,
                original_pos,
                ..
            } => {
                assert_eq!(object_id, "srv_1");
                assert_eq!(*original_pos, (1.0, 2.0, 3.0));
            }
            other => panic!("Expected UndoPickup, got {:?}", other),
        }
    }

    #[test]
    fn test_has_pending_for_object_found() {
        let mut state = WorldSyncState::new();
        state.register_pending(1, pickup_action("target"), RollbackInfo::None);

        assert!(state.has_pending_for_object("target"));
    }

    #[test]
    fn test_has_pending_for_object_not_found() {
        let mut state = WorldSyncState::new();
        state.register_pending(1, pickup_action("other"), RollbackInfo::None);

        assert!(!state.has_pending_for_object("target"));
    }

    #[test]
    fn test_has_pending_for_object_empty() {
        let state = WorldSyncState::new();
        assert!(!state.has_pending_for_object("anything"));
    }

    #[test]
    fn test_has_pending_for_object_multiple_types() {
        let mut state = WorldSyncState::new();
        state.register_pending(1, power_action("switch_1", true), RollbackInfo::None);
        state.register_pending(
            2,
            WorldAction::PropertyChanged {
                object_id: "srv_2".into(),
                key: "hostname".into(),
                value: "test".into(),
            },
            RollbackInfo::None,
        );
        state.register_pending(
            3,
            WorldAction::InstalledInRack {
                object_id: "srv_3".into(),
                object_type: 1,
                rack_position_uid: 42,
            },
            RollbackInfo::None,
        );

        assert!(state.has_pending_for_object("switch_1"));
        assert!(state.has_pending_for_object("srv_2"));
        assert!(state.has_pending_for_object("srv_3"));
        assert!(!state.has_pending_for_object("nonexistent"));
    }

    #[test]
    fn test_has_pending_for_object_ignores_cables() {
        let mut state = WorldSyncState::new();
        state.register_pending(1, cable_action(99), RollbackInfo::None);

        assert!(!state.has_pending_for_object("dev_a"));
        assert!(!state.has_pending_for_object("dev_b"));
        assert!(!state.has_pending_for_object("99"));
    }

    #[test]
    fn test_has_pending_for_object_cable_disconnected() {
        let mut state = WorldSyncState::new();
        state.register_pending(
            1,
            WorldAction::CableDisconnected { cable_id: 7 },
            RollbackInfo::None,
        );

        assert!(!state.has_pending_for_object("7"));
        assert!(!state.has_pending_for_object("anything"));
    }

    #[test]
    fn test_has_pending_for_object_all_object_variants() {
        let mut state = WorldSyncState::new();
        let id = "test_obj";

        state.register_pending(
            1,
            WorldAction::ObjectPickedUp {
                object_id: id.into(),
                object_type: 1,
            },
            RollbackInfo::None,
        );
        state.register_pending(
            2,
            WorldAction::ObjectDropped {
                object_id: id.into(),
                object_type: 1,
                pos_x: 0.0,
                pos_y: 0.0,
                pos_z: 0.0,
                rot_x: 0.0,
                rot_y: 0.0,
                rot_z: 0.0,
                rot_w: 1.0,
            },
            RollbackInfo::None,
        );
        state.register_pending(
            3,
            WorldAction::InstalledInRack {
                object_id: id.into(),
                object_type: 1,
                rack_position_uid: 0,
            },
            RollbackInfo::None,
        );
        state.register_pending(
            4,
            WorldAction::RemovedFromRack {
                object_id: id.into(),
                object_type: 1,
            },
            RollbackInfo::None,
        );
        state.register_pending(
            5,
            WorldAction::PowerToggled {
                object_id: id.into(),
                is_on: true,
            },
            RollbackInfo::None,
        );
        state.register_pending(
            6,
            WorldAction::PropertyChanged {
                object_id: id.into(),
                key: "k".into(),
                value: "v".into(),
            },
            RollbackInfo::None,
        );
        state.register_pending(
            7,
            WorldAction::ObjectSpawned {
                object_id: id.into(),
                object_type: 1,
                prefab_id: 0,
                pos_x: 0.0,
                pos_y: 0.0,
                pos_z: 0.0,
                rot_x: 0.0,
                rot_y: 0.0,
                rot_z: 0.0,
                rot_w: 1.0,
            },
            RollbackInfo::None,
        );
        state.register_pending(
            8,
            WorldAction::ObjectDestroyed {
                object_id: id.into(),
                object_type: 1,
            },
            RollbackInfo::None,
        );

        assert!(state.has_pending_for_object(id));

        for seq in 1..=7 {
            state.remove_pending(seq);
            assert!(state.has_pending_for_object(id));
        }
        state.remove_pending(8);
        assert!(!state.has_pending_for_object(id));
    }

    #[test]
    fn test_last_known_hashes_crud() {
        let mut state = WorldSyncState::new();
        assert!(state.last_known_hashes.is_empty());

        state.last_known_hashes.insert("obj_1".into(), 0xDEAD);
        state.last_known_hashes.insert("obj_2".into(), 0xBEEF);
        assert_eq!(state.last_known_hashes.len(), 2);
        assert_eq!(state.last_known_hashes["obj_1"], 0xDEAD);
        assert_eq!(state.last_known_hashes["obj_2"], 0xBEEF);

        state.last_known_hashes.insert("obj_1".into(), 0xCAFE);
        assert_eq!(state.last_known_hashes["obj_1"], 0xCAFE);
        assert_eq!(state.last_known_hashes.len(), 2);
    }

    #[test]
    fn test_reset_clears_everything() {
        let mut state = WorldSyncState::new();

        state.next_seq = 500;
        state.game_time = 99.0;
        state.hash_check_timer = 15.0;
        state.last_known_hashes.insert("obj_1".into(), 42);
        state.last_known_hashes.insert("obj_2".into(), 43);
        state.register_pending(500, pickup_action("a"), RollbackInfo::None);
        state.register_pending(501, pickup_action("b"), RollbackInfo::None);

        state.reset();

        assert_eq!(state.next_seq, 1);
        assert!(state.pending_actions.is_empty());
        assert_eq!(state.hash_check_timer, 0.0);
        assert!(state.last_known_hashes.is_empty());
        assert_eq!(state.game_time, 0.0);
    }

    #[test]
    fn test_reset_allows_reuse() {
        let mut state = WorldSyncState::new();
        state.game_time = 50.0;
        state.register_pending(1, pickup_action("a"), RollbackInfo::None);

        state.reset();

        let seq = state.next_seq();
        assert_eq!(seq, 1);
        state.game_time = 1.0;
        state.register_pending(seq, pickup_action("b"), RollbackInfo::None);
        assert_eq!(state.pending_actions.len(), 1);
        assert_eq!(state.pending_actions[0].seq, 1);
    }

    #[test]
    fn test_full_action_lifecycle() {
        let mut state = WorldSyncState::new();

        state.game_time = 10.0;
        let seq = state.next_seq();
        assert_eq!(seq, 1);
        state.register_pending(
            seq,
            power_action("switch_1", true),
            RollbackInfo::UndoPowerToggle {
                object_id: "switch_1".into(),
                was_on: false,
            },
        );

        assert!(state.has_pending_for_object("switch_1"));
        assert_eq!(state.pending_actions.len(), 1);

        state.game_time = 11.0;
        let acked = state.remove_pending(seq);
        assert!(acked.is_some());
        assert!(!state.has_pending_for_object("switch_1"));
        assert!(state.pending_actions.is_empty());

        let timed_out = state.drain_timed_out();
        assert!(timed_out.is_empty());
    }

    #[test]
    fn test_rejected_action_lifecycle() {
        let mut state = WorldSyncState::new();

        state.game_time = 0.0;
        let seq = state.next_seq();
        state.register_pending(
            seq,
            pickup_action("srv_1"),
            RollbackInfo::UndoPickup {
                object_id: "srv_1".into(),
                object_type: 1,
                original_pos: (5.0, 0.5, 3.0),
                original_rot: (0.0, 0.0, 0.0, 1.0),
            },
        );

        let rejected = state.remove_pending(seq).unwrap();
        match &rejected.rollback_info {
            RollbackInfo::UndoPickup { original_pos, .. } => {
                assert_eq!(*original_pos, (5.0, 0.5, 3.0));
            }
            other => panic!("Expected UndoPickup, got {:?}", other),
        }
    }

    #[test]
    fn test_timeout_then_new_actions() {
        let mut state = WorldSyncState::new();

        state.game_time = 0.0;
        let old_seq = state.next_seq();
        state.register_pending(old_seq, pickup_action("old_obj"), RollbackInfo::None);

        state.game_time = 4.0;
        let new_seq = state.next_seq();
        state.register_pending(new_seq, pickup_action("new_obj"), RollbackInfo::None);

        state.game_time = 6.0;
        let timed_out = state.drain_timed_out();
        assert_eq!(timed_out.len(), 1);
        assert_eq!(timed_out[0].seq, old_seq);

        assert_eq!(state.pending_actions.len(), 1);
        assert_eq!(state.pending_actions[0].seq, new_seq);
        assert!(state.has_pending_for_object("new_obj"));
        assert!(!state.has_pending_for_object("old_obj"));
    }
}
