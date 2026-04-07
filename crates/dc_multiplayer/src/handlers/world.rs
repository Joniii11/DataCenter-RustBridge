use crate::protocol::{Message, ObjectHash, WorldAction};
use crate::state::*;
use crate::world;
use dc_api::Api;

pub(super) fn handle_world_action_msg(api: &Api, sender: u64, seq: u32, action: WorldAction) {
    let is_host = with_state(|s| s.session.is_host).unwrap_or(false);
    if !is_host {
        return;
    }

    dc_api::crash_log(&format!(
        "[MP] WorldActionMsg seq={} from {}: {} '{}'",
        seq,
        sender,
        action.tag(),
        action.object_id()
    ));

    let accepted = validate_world_action(api, &action);

    let ack = Message::WorldActionAck { seq, accepted };
    with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            relay.send_game_message_to(&ack, sender);
        }
    });

    if accepted {
        let broadcast = Message::WorldActionBroadcast {
            action: action.clone(),
        };
        with_state(|s| {
            if let Some(ref relay) = s.session.relay {
                s.tracker.for_each_player_mut(|player| {
                    if player.steam_id != sender {
                        relay.send_game_message_to(&broadcast, player.steam_id);
                    }
                });
            }
        });

        world::execute_world_action(api, &action);
    }
}

pub(super) fn handle_world_action_ack(api: &Api, seq: u32, accepted: bool) {
    let is_host = with_state(|s| s.session.is_host).unwrap_or(false);
    if is_host {
        return;
    }

    dc_api::crash_log(&format!(
        "[MP] WorldActionAck seq={} accepted={}",
        seq, accepted
    ));

    let pending = with_state(|s| s.world_sync.remove_pending(seq)).flatten();

    if let Some(pending_action) = pending {
        if !accepted {
            dc_api::crash_log(&format!("[MP] Action seq={} rejected, rolling back", seq));
            world::execute_rollback(api, &pending_action.rollback_info);
            api.show_notification("Action rejected by host.");
        }
    } else {
        dc_api::crash_log(&format!(
            "[MP] WorldActionAck seq={} but no pending action found (already timed out?)",
            seq
        ));
    }
}

pub(super) fn handle_world_action_broadcast(api: &Api, sender: u64, action: WorldAction) {
    let is_host = with_state(|s| s.session.is_host).unwrap_or(false);
    if is_host {
        return;
    }

    dc_api::crash_log(&format!(
        "[MP] WorldActionBroadcast from {}: {} '{}'",
        sender,
        action.tag(),
        action.object_id()
    ));

    world::execute_world_action(api, &action);
}

// TODO Phase 4: Client compares hashes, requests resync for mismatches
pub(super) fn handle_hash_check(hashes: Vec<ObjectHash>) {
    dc_api::crash_log(&format!(
        "[MP] Received WorldHashCheck with {} hashes (not yet implemented)",
        hashes.len()
    ));
}

// TODO Phase 4: Host sends full object state back
pub(super) fn handle_resync_request(object_id: String) {
    dc_api::crash_log(&format!(
        "[MP] Received WorldResyncRequest for '{}' (not yet implemented)",
        object_id
    ));
}

// TODO Phase 4: Client applies authoritative object state
pub(super) fn handle_resync_response(object_id: String, object_type: u8, data: Vec<u8>) {
    dc_api::crash_log(&format!(
        "[MP] Received WorldResyncResponse for '{}' type={} ({} bytes) (not yet implemented)",
        object_id,
        object_type,
        data.len()
    ));
}

fn validate_world_action(_api: &Api, action: &WorldAction) -> bool {
    let (valid, reason) = match action {
        WorldAction::InstalledInRack {
            object_id,
            rack_position_uid,
            ..
        } => {
            if object_id.is_empty() {
                (false, "InstalledInRack: empty object_id")
            } else if *rack_position_uid < 0 {
                (false, "InstalledInRack: negative rack_position_uid")
            } else {
                (true, "InstalledInRack: valid")
            }
        }
        WorldAction::ObjectDropped {
            object_id,
            pos_x,
            pos_y,
            pos_z,
            rot_x,
            rot_y,
            rot_z,
            rot_w,
            ..
        } => {
            if object_id.is_empty() {
                (false, "ObjectDropped: empty object_id")
            } else if *pos_x == 0.0
                && *pos_y == 0.0
                && *pos_z == 0.0
                && *rot_x == 0.0
                && *rot_y == 0.0
                && *rot_z == 0.0
                && *rot_w == 0.0
            {
                (
                    false,
                    "ObjectDropped: all position/rotation values are zero (uninitialized)",
                )
            } else {
                (true, "ObjectDropped: valid")
            }
        }
        WorldAction::ObjectPickedUp { object_id, .. }
        | WorldAction::RemovedFromRack { object_id, .. }
        | WorldAction::PowerToggled { object_id, .. }
        | WorldAction::PropertyChanged { object_id, .. }
        | WorldAction::ObjectSpawned { object_id, .. }
        | WorldAction::ObjectDestroyed { object_id, .. } => {
            if object_id.is_empty() {
                (false, "empty object_id")
            } else {
                (true, "valid")
            }
        }
        WorldAction::CableConnected { .. } | WorldAction::CableDisconnected { .. } => {
            (true, "cable action: accepted")
        }
    };

    dc_api::crash_log(&format!(
        "[MP] Validating {} '{}' → {} ({})",
        action.tag(),
        action.object_id(),
        if valid { "accepted" } else { "rejected" },
        reason
    ));
    valid
}
