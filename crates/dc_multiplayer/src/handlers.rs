//! Relay event processing and game message handling.

use crate::net;
use crate::player;
use crate::protocol::Message;
use crate::state::*;
use dc_api::Api;

/// Process a relay event (room created, peer joined, game message, etc.).
pub fn process_relay_event(api: &Api, event: net::RelayEvent) {
    match event {
        net::RelayEvent::RoomCreated(code) => {
            dc_api::crash_log(&format!("[MP] Room created with code: {}", code));
            api.log_info(&format!("[MP] Room code: {}", code));
            with_state(|s| {
                s.room_code = Some(code);
            });
        }

        net::RelayEvent::JoinOk { host_steam_id } => {
            dc_api::crash_log(&format!(
                "[MP] Joined room, host steam ID: {}",
                host_steam_id
            ));
            api.log_info(&format!("[MP] Joined room (host: {})", host_steam_id));

            with_state(|s| {
                s.peer_id = host_steam_id;
                s.join_ok_received = true;
                s.hello_retry_timer = 0.0;
                s.hello_retry_count = 0;
            });

            let msg = Message::Hello {
                player_name: get_my_steam_name(api),
                mod_version: "0.1.0".to_string(),
            };
            let ok = with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&msg)
                } else {
                    false
                }
            })
            .unwrap_or(false);

            if ok {
                dc_api::crash_log("[MP] Sent Hello to host via relay");
            } else {
                dc_api::crash_log("[MP] Failed to send Hello to host via relay");
            }
        }

        net::RelayEvent::RoomNotFound => {
            dc_api::crash_log("[MP] Room not found!");
            api.log_error("[MP] Room not found. Check the room code and try again.");
            api.show_notification("Room not found!");
            for eid in do_disconnect_cleanup() {
                api.destroy_entity(eid);
            }
        }

        net::RelayEvent::RoomFull => {
            dc_api::crash_log("[MP] Room is full!");
            api.log_error("[MP] Room is full.");
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
                if s.peer_id == steam_id {
                    s.peer_id = 0;
                    s.connected = false;
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

        net::RelayEvent::GameMessage { sender, message } => {
            handle_message(api, sender, message);
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

/// Resolve the local player's Steam display name, with fallback.
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

// ── Game message handler ────────────────────────────────────────────────────

fn handle_message(api: &Api, sender: u64, msg: Message) {
    match msg {
        Message::Hello {
            player_name,
            mod_version,
        } => {
            api.log_info(&format!(
                "[MP] {} ({}) wants to connect (v{})",
                player_name, sender, mod_version
            ));

            with_state(|s| {
                s.peer_id = sender;
                s.connected = true;
                s.connecting = false;
                s.tracker.add_player(sender, player_name.clone());
            });

            let my_name = get_my_steam_name(api);
            let is_host = with_state(|s| s.is_host).unwrap_or(false);
            let welcome = Message::Welcome {
                player_name: my_name,
                is_host,
            };
            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&welcome);
                }
            });

            api.show_notification(&format!("{} joined!", player_name));
        }

        Message::Welcome {
            player_name,
            is_host: _,
        } => {
            api.log_info(&format!("[MP] Connected to {} ({})", player_name, sender));

            with_state(|s| {
                s.connected = true;
                s.connecting = false;
                s.join_ok_received = false;
                s.tracker.add_player(sender, player_name.clone());
            });

            let should_request = with_state(|s| {
                if s.skip_next_save_request {
                    s.skip_next_save_request = false;
                    s.join_state = JoinState::Loaded;
                    dc_api::crash_log(
                        "[MP] Skipping save request (reconnect mode) — join_state → Loaded",
                    );
                    false
                } else {
                    s.join_state = JoinState::WaitingForSave;
                    dc_api::crash_log("[MP] join_state → WaitingForSave");
                    true
                }
            })
            .unwrap_or(true);

            if should_request {
                let req = Message::RequestSave;
                with_state(|s| {
                    if let Some(ref relay) = s.relay {
                        relay.send_game_message(&req);
                        dc_api::crash_log("[MP] Sent RequestSave to host");
                    }
                });
            }

            api.show_notification(&format!("Connected to {}!", player_name));
        }

        Message::Position { x, y, z, rot_y } => {
            with_state(|s| {
                if !s.tracker.has_player(sender) {
                    let fallback_name = api
                        .steam_get_friend_name(sender)
                        .unwrap_or_else(|| format!("Player_{}", sender % 10000));
                    s.tracker.add_player(sender, fallback_name);
                }
                s.tracker.update_position(sender, x, y, z, rot_y);
            });
        }

        Message::Goodbye => {
            let (name, entity_id) = with_state(|s| {
                let eid = s.tracker.remove_player_with_entity(sender);
                if s.peer_id == sender {
                    s.peer_id = 0;
                    s.connected = false;
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

        Message::Ping(ts) => {
            let pong = Message::Pong(ts);
            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&pong);
                }
            });
        }

        Message::Pong(_ts) => {
            // Could calculate RTT here
        }

        Message::RequestSave => {
            dc_api::crash_log(&format!("[MP] Save requested by peer {}", sender));
            with_state(|s| {
                if s.is_host {
                    s.save_requested = true;
                }
            });
        }

        Message::SaveOffer {
            total_bytes,
            chunk_count,
            save_hash,
        } => {
            dc_api::crash_log(&format!(
                "[MP] Received SaveOffer: {} bytes in {} chunks (hash: {:016x})",
                total_bytes, chunk_count, save_hash
            ));
            with_state(|s| {
                if !s.is_host {
                    if s.local_save_hash != 0 && s.local_save_hash == save_hash {
                        dc_api::crash_log(&format!(
                            "[MP] Save hash match! Local save is up to date (hash: {:016x})",
                            save_hash
                        ));
                        s.save_up_to_date = true;
                        s.join_state = JoinState::SaveUpToDate;
                        if let Some(ref relay) = s.relay {
                            relay.send_game_message(&Message::SaveSkip);
                        }
                        return;
                    }

                    s.save_up_to_date = false;
                    s.save_incoming_total = total_bytes;
                    s.save_incoming_chunk_count = chunk_count;
                    s.save_incoming_data = vec![0u8; total_bytes as usize];
                    s.save_incoming_received = vec![false; chunk_count as usize];
                    s.save_data_ready = None;
                }
            });
        }

        Message::SaveChunk { index, data } => {
            with_state(|s| {
                if s.is_host || s.save_up_to_date {
                    return;
                }
                if index as usize >= s.save_incoming_received.len() {
                    return;
                }

                let offset = index as usize * SAVE_CHUNK_SIZE;
                let end = (offset + data.len()).min(s.save_incoming_data.len());
                if offset < s.save_incoming_data.len() {
                    s.save_incoming_data[offset..end].copy_from_slice(&data[..end - offset]);
                }
                s.save_incoming_received[index as usize] = true;

                let received_count = s.save_incoming_received.iter().filter(|&&r| r).count();
                dc_api::crash_log(&format!(
                    "[MP] Save chunk {}/{} received ({} bytes)",
                    received_count,
                    s.save_incoming_chunk_count,
                    data.len()
                ));

                if s.save_incoming_received.iter().all(|&r| r) {
                    dc_api::crash_log(&format!(
                        "[MP] All save chunks received! Total: {} bytes",
                        s.save_incoming_total
                    ));
                    let complete = std::mem::take(&mut s.save_incoming_data);
                    s.save_data_ready = Some(complete);
                    s.save_incoming_received.clear();
                    s.join_state = JoinState::SaveReady;
                    dc_api::crash_log("[MP] join_state SaveReady");
                }
            });
        }

        Message::SaveSkip => {
            dc_api::crash_log(&format!(
                "[MP] Peer {} says save is up to date, stopping chunks",
                sender
            ));
            with_state(|s| {
                if s.is_host {
                    s.save_outgoing = None;
                    s.save_send_index = 0;
                    s.save_send_chunk_count = 0;
                }
            });
        }
    }
}

// ── Disconnect cleanup ──────────────────────────────────────────────────────

/// Full disconnect cleanup. Returns entity IDs that need to be destroyed.
pub fn do_disconnect_cleanup() -> Vec<u32> {
    with_state(|s| {
        let entity_ids = s.tracker.get_all_entity_ids();

        if let Some(ref relay) = s.relay {
            relay.disconnect();
        }
        s.relay = None;
        s.peer_id = 0;
        s.connected = false;
        s.connecting = false;
        s.is_host = false;
        s.room_code = None;
        s.room_code_cstr = None;
        s.join_ok_received = false;
        s.hello_retry_timer = 0.0;
        s.hello_retry_count = 0;
        s.tracker = player::PlayerTracker::new();

        s.save_requested = false;
        s.save_outgoing = None;
        s.save_send_index = 0;
        s.save_send_chunk_count = 0;
        s.save_incoming_total = 0;
        s.save_incoming_chunk_count = 0;
        s.save_incoming_data.clear();
        s.save_incoming_received.clear();
        s.save_data_ready = None;
        s.save_loaded = false;
        s.skip_next_save_request = false;
        s.save_up_to_date = false;
        s.join_state = JoinState::Idle;

        entity_ids
    })
    .unwrap_or_default()
}
