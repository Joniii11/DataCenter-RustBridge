mod carry;
mod entities;

use crate::handlers;
use crate::net;
use crate::protocol::Message;
use crate::state::*;
use dc_api::Api;

pub type SaveChunks = (u64, Vec<(u32, Vec<u8>)>);

const ROOF_CHECK_INTERVAL: f32 = 2.0;
const ROOF_Y_THRESHOLD: f32 = 3.5;

pub fn update(api: &Api, dt: f32) {
    let my_id = with_state(|s| s.session.my_id).unwrap_or(0);
    if my_id == 0 {
        if let Some(id) = api.steam_get_my_id() {
            with_state(|s| s.session.my_id = id);
            api.log_info(&format!("[MP] My Steam ID: {}", id));
        }
    }

    let events: Vec<net::RelayEvent> = with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            relay.poll_events()
        } else {
            Vec::new()
        }
    })
    .unwrap_or_default();

    for event in events {
        handlers::process_relay_event(api, event);
    }

    let retry_needed = with_state(|s| {
        if s.session.is_host
            || !s.session.connecting
            || s.session.connected
            || !s.session.join_ok_received
        {
            return None;
        }

        s.session.hello_retry_timer += dt;
        if s.session.hello_retry_timer >= HELLO_RETRY_INTERVAL {
            s.session.hello_retry_timer = 0.0;
            s.session.hello_retry_count += 1;

            if s.session.hello_retry_count > HELLO_MAX_RETRIES {
                dc_api::crash_log("[MP] Hello retry limit reached, giving up.");
                s.session.connecting = false;
                s.session.join_ok_received = false;
                return None;
            }

            Some(s.session.hello_retry_count)
        } else {
            None
        }
    })
    .flatten();

    if let Some(count) = retry_needed {
        let msg = Message::Hello {
            player_name: handlers::get_my_steam_name(api),
            mod_version: "0.1.0".to_string(),
        };
        let ok = with_state(|s| {
            if let Some(ref relay) = s.session.relay {
                relay.send_game_message_to(&msg, s.session.peer_id)
            } else {
                false
            }
        })
        .unwrap_or(false);

        if ok {
            dc_api::crash_log(&format!("[MP] Hello retry #{} sent (queued OK)", count));
        } else {
            dc_api::crash_log(&format!("[MP] Hello retry #{} FAILED (will retry)", count));
        }
    }

    let connected = with_state(|s| s.session.connected).unwrap_or(false);
    if !connected {
        return;
    }

    check_world_action_timeouts(api, dt);

    let need_rack_uids =
        with_state(|s| s.session.is_host && !s.session.rack_uids_ensured).unwrap_or(false);
    if need_rack_uids {
        let assigned = api.world_ensure_rack_uids();
        if assigned > 0 {
            with_state(|s| s.session.rack_uids_ensured = true);
            dc_api::crash_log(&format!(
                "[MP] Host lazy-ensured {} rack position UIDs",
                assigned
            ));
        }
    }

    let should_send = with_state(|s| {
        s.session.pos_timer += dt;
        if s.session.pos_timer >= POSITION_SEND_INTERVAL {
            s.session.pos_timer = 0.0;
            true
        } else {
            false
        }
    })
    .unwrap_or(false);

    if should_send {
        if let Some((x, y, z, ry)) = api.get_player_position() {
            let msg = Message::Position { x, y, z, rot_y: ry };
            with_state(|s| {
                if let Some(ref relay) = s.session.relay {
                    relay.send_game_message(&msg);
                }
            });
        }
    }

    if api.version() >= 10 {
        let current_state = {
            let carry = api.get_player_carry_state().unwrap_or((0, 0));
            let crouching = api.get_player_crouching().unwrap_or(false);
            let sitting = api.get_player_sitting().unwrap_or(false);
            crate::player::PlayerStateSnapshot {
                object_in_hand: carry.0 as u8,
                num_objects: carry.1 as u8,
                is_crouching: crouching,
                is_sitting: sitting,
            }
        };

        let should_send_state = with_state(|s| {
            s.session.player_state_heartbeat_timer += dt;
            let changed = current_state != s.session.last_sent_player_state;
            let heartbeat =
                s.session.player_state_heartbeat_timer >= PLAYER_STATE_HEARTBEAT_INTERVAL;
            if changed || heartbeat {
                s.session.last_sent_player_state = current_state;
                s.session.player_state_heartbeat_timer = 0.0;
                true
            } else {
                false
            }
        })
        .unwrap_or(false);

        if should_send_state {
            let msg = Message::PlayerState {
                object_in_hand: current_state.object_in_hand,
                num_objects: current_state.num_objects,
                is_crouching: current_state.is_crouching,
                is_sitting: current_state.is_sitting,
            };
            with_state(|s| {
                if let Some(ref relay) = s.session.relay {
                    relay.send_game_message(&msg);
                }
            });
        }

        carry::detect_carry_transitions(api, current_state.num_objects);
    }

    let targeted_chunks: Vec<SaveChunks> = with_state(|s| {
        if !s.session.is_host {
            return Vec::new();
        }
        let outgoing = match s.save.outgoing.as_ref() {
            Some(d) => d,
            None => return Vec::new(),
        };

        let mut all_chunks = Vec::new();
        let mut completed = Vec::new();
        let max_per_frame = 5;

        for (&peer_id, transfer) in s.save.transfers.iter_mut() {
            let mut peer_chunks = Vec::new();
            for _ in 0..max_per_frame {
                if transfer.send_index >= s.save.chunk_count {
                    completed.push(peer_id);
                    break;
                }
                let offset = transfer.send_index as usize * SAVE_CHUNK_SIZE;
                let end = (offset + SAVE_CHUNK_SIZE).min(outgoing.len());
                peer_chunks.push((transfer.send_index, outgoing[offset..end].to_vec()));
                transfer.send_index += 1;
            }
            if !peer_chunks.is_empty() {
                all_chunks.push((peer_id, peer_chunks));
            }
        }

        for id in completed {
            s.save.transfers.remove(&id);
            dc_api::crash_log(&format!("[MP] All save chunks sent to peer {}", id));
        }

        if s.save.transfers.is_empty() && s.save.outgoing.is_some() {
            s.save.outgoing = None;
            dc_api::crash_log("[MP] All save transfers complete, cleared outgoing data");
        }

        all_chunks
    })
    .unwrap_or_default();

    for (target, chunks) in targeted_chunks {
        for (index, data) in chunks {
            let msg = Message::SaveChunk { index, data };
            with_state(|s| {
                if let Some(ref relay) = s.session.relay {
                    relay.send_game_message_to(&msg, target);
                }
            });
        }
    }

    let stale_entities: Vec<(u64, Option<u32>)> =
        with_state(|s| s.tracker.cleanup_stale_with_entities()).unwrap_or_default();

    for (steam_id, entity_id) in &stale_entities {
        dc_api::crash_log(&format!("[MP] player {} timed out", steam_id));
        if let Some(eid) = entity_id {
            api.destroy_entity(*eid);
        }
    }

    if api.version() < 9 {
        return;
    }

    let (is_host, join_state) = with_state(|s| (s.session.is_host, s.session.join_state))
        .unwrap_or((false, JoinState::Idle));
    if !is_host && join_state != JoinState::Loaded {
        return;
    }

    entities::update_entities(api, dt);

    roof_safety_check(api, dt);

    entities::entity_lifecycle(api);
}

fn check_world_action_timeouts(api: &Api, dt: f32) {
    let timed_out = with_state(|s| {
        s.world_sync.game_time += dt;
        s.world_sync.drain_timed_out()
    });

    if let Some(timed_out) = timed_out {
        for pending in &timed_out {
            dc_api::crash_log(&format!(
                "[WORLD] Action seq={} timed out after {:.1}s, rolling back",
                pending.seq,
                crate::world::WORLD_ACTION_TIMEOUT_SECS
            ));
            crate::world::execute_rollback(api, &pending.rollback_info);
        }
    }
}

static mut ROOF_CHECK_TIMER: f32 = 0.0;

fn roof_safety_check(api: &Api, dt: f32) {
    let has_entities = with_state(|s| s.tracker.player_count() > 0).unwrap_or(false);
    if !has_entities {
        return;
    }

    unsafe { ROOF_CHECK_TIMER += dt };
    if unsafe { ROOF_CHECK_TIMER } < ROOF_CHECK_INTERVAL {
        return;
    }
    unsafe { ROOF_CHECK_TIMER = 0.0 };

    if let Some((_, y, _, _)) = api.get_player_position() {
        if y > ROOF_Y_THRESHOLD {
            dc_api::crash_log(&format!(
                "[MP] Roof safety net: player Y={:.2}, warping to (5, 1, -24)",
                y
            ));
            api.warp_local_player(5.0, 1.0, -24.0);
        }
    }
}
