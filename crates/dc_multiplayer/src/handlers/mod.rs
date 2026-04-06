mod save;
mod session;
mod world;

use crate::net;
use crate::protocol::Message;
use crate::state::*;
use dc_api::Api;

pub fn process_relay_event(api: &Api, event: net::RelayEvent) {
    match event {
        net::RelayEvent::RoomCreated(code) => {
            dc_api::crash_log(&format!("[MP] Room created with code: {}", code));
            with_state(|s| {
                s.session.room_code = Some(code);
            });
        }

        net::RelayEvent::JoinOk { host_steam_id } => {
            dc_api::crash_log(&format!(
                "[MP] Joined room, host steam ID: {}",
                host_steam_id
            ));

            with_state(|s| {
                s.session.peer_id = host_steam_id;
                s.session.join_ok_received = true;
                s.session.hello_retry_timer = 0.0;
                s.session.hello_retry_count = 0;
            });

            let msg = Message::Hello {
                player_name: get_my_steam_name(api),
                mod_version: "0.1.0".to_string(),
            };

            with_state(|s| {
                if let Some(ref relay) = s.session.relay {
                    relay.send_game_message_to(&msg, s.session.peer_id);
                    dc_api::crash_log("[MP] Sent Hello to host via relay");
                } else {
                    dc_api::crash_log("[MP] Failed to send Hello to host via relay");
                }
            });
        }

        net::RelayEvent::RoomNotFound => {
            dc_api::crash_log("[MP] Room not found!");
            api.show_notification("Room not found!");
            for eid in do_disconnect_cleanup() {
                api.destroy_entity(eid);
            }
        }

        net::RelayEvent::RoomFull => {
            dc_api::crash_log("[MP] Room is full!");
            api.show_notification("Room is full!");
            for eid in do_disconnect_cleanup() {
                api.destroy_entity(eid);
            }
        }

        net::RelayEvent::PeerJoined(steam_id) => {
            dc_api::crash_log(&format!("[MP] Peer joined: {}", steam_id));
            api.log_info(&format!("[MP] Peer {} joined the room", steam_id));
        }

        net::RelayEvent::PeerLeft(steam_id) => {
            dc_api::crash_log(&format!("[MP] Peer left: {}", steam_id));

            let entity_id = with_state(|s| {
                let eid = s.tracker.remove_player_with_entity(steam_id);

                if s.session.peer_id == steam_id {
                    s.session.peer_id = 0;
                    s.session.connected = false;
                }

                s.save.transfers.remove(&steam_id);
                if s.save.transfers.is_empty() {
                    s.save.outgoing = None;
                    s.save.chunk_count = 0;
                }
                eid
            })
            .flatten();

            if let Some(eid) = entity_id {
                api.destroy_entity(eid);
            }

            api.log_info(&format!("[MP] Peer {} left", steam_id));
            api.show_notification("Player disconnected.");
        }

        net::RelayEvent::GameMessage {
            sender,
            target,
            message,
        } => {
            handle_message(api, sender, target, message);
        }

        net::RelayEvent::Error(msg) => {
            dc_api::crash_log(&format!("[MP] Relay error: {}", msg));
            api.log_error(&format!("[MP] Error: {}", msg));
        }

        net::RelayEvent::Disconnected => {
            dc_api::crash_log("[MP] Disconnected from relay server");
            api.log_error("[MP] Lost connection to relay server.");
            api.show_notification("Disconnected from server.");
            for eid in do_disconnect_cleanup() {
                api.destroy_entity(eid);
            }
        }
    }
}

pub fn get_my_steam_name(api: &Api) -> String {
    if let Some(my_id) = api.steam_get_my_id() {
        if let Some(name) = api.steam_get_friend_name(my_id) {
            if !name.is_empty() {
                return name;
            }
        }
    }
    "Player".to_string()
}

pub fn do_disconnect_cleanup() -> Vec<u32> {
    with_state(|s| {
        let entity_ids = s.tracker.get_all_entity_ids();

        if let Some(ref relay) = s.session.relay {
            relay.disconnect();
        }
        s.session.relay = None;
        s.session.peer_id = 0;
        s.session.connected = false;
        s.session.connecting = false;
        s.session.is_host = false;
        s.session.room_code = None;
        s.session.room_code_cstr = None;
        s.session.join_ok_received = false;
        s.session.hello_retry_timer = 0.0;
        s.session.hello_retry_count = 0;
        s.session.join_state = JoinState::Idle;
        s.session.default_spawn = None;
        s.session.last_sent_player_state = crate::player::PlayerStateSnapshot::default();
        s.session.player_state_heartbeat_timer = 0.0;

        s.tracker = crate::player::PlayerTracker::new();

        s.save.transfers.clear();
        s.save.outgoing = None;
        s.save.chunk_count = 0;
        s.save.incoming_total = 0;
        s.save.incoming_chunk_count = 0;
        s.save.incoming_data.clear();
        s.save.incoming_received.clear();
        s.save.data_ready = None;
        s.save.loaded = false;
        s.save.skip_next_request = false;
        s.save.up_to_date = false;

        s.world_sync.reset();

        entity_ids
    })
    .unwrap_or_default()
}

fn handle_message(api: &Api, sender: u64, target: u64, msg: Message) {
    let my_id = with_state(|s| s.session.my_id).unwrap_or(0);
    if target != 0 && my_id != 0 && target != my_id {
        return;
    }

    match msg {
        Message::Hello {
            player_name,
            mod_version,
        } => session::handle_hello(api, sender, player_name, mod_version),

        Message::Welcome {
            player_name,
            is_host: _,
            spawn_x,
            spawn_y,
            spawn_z,
        } => session::handle_welcome(api, sender, player_name, spawn_x, spawn_y, spawn_z),

        Message::Position { x, y, z, rot_y } => {
            session::handle_position(api, sender, x, y, z, rot_y)
        }

        Message::Goodbye => session::handle_goodbye(api, sender),

        Message::PlayerState {
            object_in_hand,
            num_objects,
            is_crouching,
            is_sitting,
        } => session::handle_player_state(
            sender,
            object_in_hand,
            num_objects,
            is_crouching,
            is_sitting,
        ),

        Message::Ping(ts) => session::handle_ping(sender, ts),
        Message::Pong(ts) => session::handle_pong(ts),

        Message::RequestSave => save::handle_request_save(sender),

        Message::SaveOffer {
            total_bytes,
            chunk_count,
            save_hash,
        } => save::handle_save_offer(sender, total_bytes, chunk_count, save_hash),

        Message::SaveChunk { index, data } => save::handle_save_chunk(index, data),

        Message::SaveSkip => save::handle_save_skip(sender),

        Message::WorldActionMsg { seq, action } => {
            world::handle_world_action_msg(api, sender, seq, action)
        }

        Message::WorldActionAck { seq, accepted } => {
            world::handle_world_action_ack(api, seq, accepted)
        }

        Message::WorldActionBroadcast { action } => {
            world::handle_world_action_broadcast(api, sender, action)
        }

        Message::WorldHashCheck { hashes } => world::handle_hash_check(hashes),

        Message::WorldResyncRequest { object_id } => world::handle_resync_request(object_id),

        Message::WorldResyncResponse {
            object_id,
            object_type,
            data,
        } => world::handle_resync_response(object_id, object_type, data),
    }
}
