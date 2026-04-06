use crate::protocol::Message;
use crate::state::*;
use dc_api::{Api, Vec3};

pub(super) fn handle_hello(api: &Api, sender: u64, player_name: String, mod_version: String) {
    let is_host = with_state(|s| s.session.is_host).unwrap_or(false);
    if !is_host {
        return;
    }

    api.log_info(&format!(
        "[MP] {} ({}) wants to connect (v{})",
        player_name, sender, mod_version
    ));

    with_state(|s| {
        s.session.peer_id = sender;
        s.session.connected = true;
        s.session.connecting = false;
        s.tracker.add_player(sender, player_name.clone());
    });

    let my_name = super::get_my_steam_name(api);
    let is_host = with_state(|s| s.session.is_host).unwrap_or(false);

    let pos = api.get_default_spawn_position().unwrap_or(Vec3::zero());

    with_state(|s| {
        s.session.default_spawn = if pos.is_zero() { None } else { Some(pos) };
    });

    dc_api::crash_log(&format!(
        "[MP] Default spawn for joining player: ({:.1}, {:.1}, {:.1})",
        pos.x, pos.y, pos.z
    ));

    let welcome = Message::Welcome {
        player_name: my_name,
        is_host,
        spawn_x: pos.x,
        spawn_y: pos.y,
        spawn_z: pos.z,
    };
    with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            relay.send_game_message_to(&welcome, sender);
        }
    });

    api.show_notification(&format!("{} joined!", player_name));
}

pub(super) fn handle_welcome(
    api: &Api,
    sender: u64,
    player_name: String,
    spawn_x: f32,
    spawn_y: f32,
    spawn_z: f32,
) {
    let is_host = with_state(|s| s.session.is_host).unwrap_or(false);
    if is_host {
        return;
    }

    api.log_info(&format!("[MP] Connected to {} ({})", player_name, sender));

    with_state(|s| {
        s.session.connected = true;
        s.session.connecting = false;
        s.session.join_ok_received = false;
        s.tracker.add_player(sender, player_name.clone());
    });

    let should_request = with_state(|s| {
        if s.save.skip_next_request {
            s.save.skip_next_request = false;
            s.session.join_state = JoinState::Loaded;
            dc_api::crash_log("[MP] Skipping save request (reconnect mode) — join_state → Loaded");
            false
        } else {
            s.session.join_state = JoinState::WaitingForSave;
            dc_api::crash_log("[MP] join_state → WaitingForSave");
            true
        }
    })
    .unwrap_or(true);

    if should_request {
        let req = Message::RequestSave;
        with_state(|s| {
            if let Some(ref relay) = s.session.relay {
                relay.send_game_message_to(&req, s.session.peer_id);
                dc_api::crash_log("[MP] Sent RequestSave to host");
            }
        });
    }

    api.show_notification(&format!("Connected to {}!", player_name));

    if spawn_x != 0.0 || spawn_y != 0.0 || spawn_z != 0.0 {
        api.warp_local_player(spawn_x, spawn_y, spawn_z);
        dc_api::crash_log(&format!(
            "[MP] Warped to default spawn ({:.1}, {:.1}, {:.1})",
            spawn_x, spawn_y, spawn_z
        ));
    }
}

pub(super) fn handle_position(api: &Api, sender: u64, x: f32, y: f32, z: f32, rot_y: f32) {
    with_state(|s| {
        if !s.tracker.has_player(sender) {
            let fallback_name = api
                .steam_get_friend_name(sender)
                .unwrap_or_else(|| format!("Player_{}", sender % 10000));
            s.tracker.add_player(sender, fallback_name);
        }
        s.tracker.update_position(sender, (x, y, z).into(), rot_y);
    });
}

pub(super) fn handle_goodbye(api: &Api, sender: u64) {
    let (name, entity_id) = with_state(|s| {
        let eid = s.tracker.remove_player_with_entity(sender);
        if s.session.peer_id == sender {
            s.session.peer_id = 0;
            s.session.connected = false;
        }
        ("Player".to_string(), eid)
    })
    .unwrap_or(("Player".to_string(), None));

    if let Some(eid) = entity_id {
        api.destroy_entity(eid);
    }
    api.log_info(&format!("[MP] {} ({}) disconnected", name, sender));
    api.show_notification("Player disconnected.");
}

pub(super) fn handle_player_state(
    sender: u64,
    object_in_hand: u8,
    num_objects: u8,
    is_crouching: bool,
    is_sitting: bool,
) {
    with_state(|s| {
        s.tracker.for_each_player_mut(|player| {
            if player.steam_id == sender {
                player.player_state = crate::player::PlayerStateSnapshot {
                    object_in_hand,
                    num_objects,
                    is_crouching,
                    is_sitting,
                };
            }
        });
    });
}

pub(super) fn handle_ping(sender: u64, ts: u64) {
    let pong = Message::Pong(ts);
    with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            relay.send_game_message_to(&pong, sender);
        }
    });
}

pub(super) fn handle_pong(_ts: u64) {}
