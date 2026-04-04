//! Relay event processing and game message handling.

use crate::net;
use crate::player;
use crate::protocol::Message;
use crate::state::*;
use dc_api::Api;
use dc_api::Vec3;

/// Process a relay event (room created, peer joined, game message, etc.).
pub fn process_relay_event(api: &Api, event: net::RelayEvent) {
    match event {
        net::RelayEvent::RoomCreated(code) => {
            dc_api::crash_log(&format!("[MP] Room created with code: {}", code));
            with_state(|s| {
                s.room_code = Some(code);
            });
        }

        net::RelayEvent::JoinOk { host_steam_id } => {
            dc_api::crash_log(&format!(
                "[MP] Joined room, host steam ID: {}",
                host_steam_id
            ));

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

            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message_to(&msg, s.peer_id);

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

                if s.peer_id == steam_id {
                    s.peer_id = 0;
                    s.connected = false;
                }

                s.save_transfers.remove(&steam_id);
                if s.save_transfers.is_empty() {
                    s.save_outgoing = None;
                    s.save_chunk_count = 0;
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

fn handle_message(api: &Api, sender: u64, target: u64, msg: Message) {
    let my_id = with_state(|s| s.my_id).unwrap_or(0);
    if target != 0 && my_id != 0 && target != my_id {
        return;
    }

    match msg {
        Message::Hello {
            player_name,
            mod_version,
        } => {
            let is_host = with_state(|s| s.is_host).unwrap_or(false);
            if !is_host {
                return;
            }

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

            let pos = api.get_default_spawn_position().unwrap_or(Vec3::zero());

            with_state(|s| {
                s.default_spawn = if pos.is_zero() { None } else { Some(pos) };
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
                if let Some(ref relay) = s.relay {
                    relay.send_game_message_to(&welcome, sender);
                }
            });

            api.show_notification(&format!("{} joined!", player_name));
        }

        Message::Welcome {
            player_name,
            is_host: _,
            spawn_x,
            spawn_y,
            spawn_z,
        } => {
            let is_host = with_state(|s| s.is_host).unwrap_or(false);
            if is_host {
                return;
            }

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
                        relay.send_game_message_to(&req, s.peer_id);
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

        Message::Position { x, y, z, rot_y } => {
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

        Message::PlayerState {
            object_in_hand,
            num_objects,
            is_crouching,
            is_sitting,
        } => {
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
                    s.save_transfers
                        .entry(sender)
                        .or_insert(crate::state::SaveTransferState { send_index: 0 });
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
                            relay.send_game_message_to(&Message::SaveSkip, sender);
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
                "[MP] Peer {} says save is up to date, stopping transfer",
                sender
            ));
            with_state(|s| {
                if s.is_host {
                    s.save_transfers.remove(&sender);

                    if s.save_transfers.is_empty() {
                        s.save_outgoing = None;
                        s.save_chunk_count = 0;
                    }
                }
            });
        }

        Message::WorldActionMsg {
            seq,
            action: _action,
        } => {
            // TODO Phase 2: Host validates action, sends ACK, broadcasts to others
            dc_api::crash_log(&format!(
                "[MP] Received WorldActionMsg seq={} from {} (not yet implemented)",
                seq, sender
            ));
        }

        Message::WorldActionAck { seq, accepted } => {
            // TODO Phase 2: Client removes pending action; if rejected, rollback
            dc_api::crash_log(&format!(
                "[MP] Received WorldActionAck seq={} accepted={} (not yet implemented)",
                seq, accepted
            ));
        }

        Message::WorldActionBroadcast { action: _action } => {
            // TODO Phase 2: Client executes authoritative action via FFI
            dc_api::crash_log(&format!(
                "[MP] Received WorldActionBroadcast from {} (not yet implemented)",
                sender
            ));
        }

        Message::WorldHashCheck { hashes } => {
            // TODO Phase 4: Client compares hashes, requests resync for mismatches
            dc_api::crash_log(&format!(
                "[MP] Received WorldHashCheck with {} hashes (not yet implemented)",
                hashes.len()
            ));
        }

        Message::WorldResyncRequest { object_id } => {
            // TODO Phase 4: Host sends full object state back
            dc_api::crash_log(&format!(
                "[MP] Received WorldResyncRequest for '{}' (not yet implemented)",
                object_id
            ));
        }

        Message::WorldResyncResponse {
            object_id,
            object_type,
            data,
        } => {
            // TODO Phase 4: Client applies authoritative object state
            dc_api::crash_log(&format!(
                "[MP] Received WorldResyncResponse for '{}' type={} ({} bytes) (not yet implemented)",
                object_id, object_type, data.len()
            ));
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

        s.save_transfers.clear();
        s.save_outgoing = None;
        s.save_chunk_count = 0;
        s.save_incoming_total = 0;
        s.save_incoming_chunk_count = 0;
        s.save_incoming_data.clear();
        s.save_incoming_received.clear();
        s.save_data_ready = None;
        s.save_loaded = false;
        s.skip_next_save_request = false;
        s.save_up_to_date = false;
        s.join_state = JoinState::Idle;
        s.last_sent_player_state = crate::player::PlayerStateSnapshot::default();
        s.player_state_heartbeat_timer = 0.0;
        s.world_sync.reset();

        entity_ids
    })
    .unwrap_or_default()
}
