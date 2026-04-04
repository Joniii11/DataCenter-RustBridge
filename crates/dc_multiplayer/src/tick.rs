//! Per-frame update logic — position sending, save chunks, entity spawning.

use crate::handlers;
use crate::net;
use crate::player::PlayerStateSnapshot;
use crate::protocol::Message;
use crate::state::*;
use dc_api::{Api, Vec3};
use std::time::Instant;

/// Interval between roof safety checks
const ROOF_CHECK_INTERVAL: f32 = 2.0;
/// Y threshold above which the player is considered stuck on the roof
const ROOF_Y_THRESHOLD: f32 = 3.5;
/// Seconds after UMA ready before collider can be added
const COLLIDER_DELAY_SECS: f32 = 3.0;
/// Minimum horizontal double distance from origin 0,0 for collider eligibility
const COLLIDER_SPAWN_DIST_SQ: f32 = 4.0;
/// Seconds before UMA mesh is considered timed out and entity is retried
const UMA_RETRY_TIMEOUT_SECS: f32 = 15.0;

/// Called every frame by the mod loader.
pub fn update(api: &Api, dt: f32) {
    // Ensure we have our Steam ID
    let my_id = with_state(|s| s.my_id).unwrap_or(0);
    if my_id == 0 {
        if let Some(id) = api.steam_get_my_id() {
            with_state(|s| s.my_id = id);
            api.log_info(&format!("[MP] My Steam ID: {}", id));
        }
    }

    // Poll relay events
    let events: Vec<net::RelayEvent> = with_state(|s| {
        if let Some(ref relay) = s.relay {
            relay.poll_events()
        } else {
            Vec::new()
        }
    })
    .unwrap_or_default();

    for event in events {
        handlers::process_relay_event(api, event);
    }

    // Hello retry logic (client connecting to host)
    let retry_needed = with_state(|s| {
        if s.is_host || !s.connecting || s.connected || !s.join_ok_received {
            return None;
        }

        s.hello_retry_timer += dt;
        if s.hello_retry_timer >= HELLO_RETRY_INTERVAL {
            s.hello_retry_timer = 0.0;
            s.hello_retry_count += 1;

            if s.hello_retry_count > HELLO_MAX_RETRIES {
                dc_api::crash_log("[MP] Hello retry limit reached, giving up.");
                s.connecting = false;
                s.join_ok_received = false;
                return None;
            }

            Some(s.hello_retry_count)
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
            if let Some(ref relay) = s.relay {
                relay.send_game_message_to(&msg, s.peer_id)
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

    // Everything below requires an active connection
    let connected = with_state(|s| s.connected).unwrap_or(false);
    if !connected {
        return;
    }

    // Send our position at fixed interval
    let should_send = with_state(|s| {
        s.pos_timer += dt;
        if s.pos_timer >= POSITION_SEND_INTERVAL {
            s.pos_timer = 0.0;
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
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&msg);
                }
            });
        }
    }

    // Send player state (on change + heartbeat)
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
            s.player_state_heartbeat_timer += dt;
            let changed = current_state != s.last_sent_player_state;
            let heartbeat = s.player_state_heartbeat_timer >= PLAYER_STATE_HEARTBEAT_INTERVAL;
            if changed || heartbeat {
                s.last_sent_player_state = current_state;
                s.player_state_heartbeat_timer = 0.0;
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
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&msg);
                }
            });
        }
    }

    let targeted_chunks: Vec<(u64, Vec<(u32, Vec<u8>)>)> = with_state(|s| {
        if !s.is_host {
            return Vec::new();
        }
        let outgoing = match s.save_outgoing.as_ref() {
            Some(d) => d,
            None => return Vec::new(),
        };

        let mut all_chunks = Vec::new();
        let mut completed = Vec::new();
        let max_per_frame = 5;

        for (&peer_id, transfer) in s.save_transfers.iter_mut() {
            let mut peer_chunks = Vec::new();
            for _ in 0..max_per_frame {
                if transfer.send_index >= s.save_chunk_count {
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
            s.save_transfers.remove(&id);
            dc_api::crash_log(&format!("[MP] All save chunks sent to peer {}", id));
        }

        if s.save_transfers.is_empty() && s.save_outgoing.is_some() {
            s.save_outgoing = None;
            dc_api::crash_log("[MP] All save transfers complete, cleared outgoing data");
        }

        all_chunks
    })
    .unwrap_or_default();

    for (target, chunks) in targeted_chunks {
        for (index, data) in chunks {
            let msg = Message::SaveChunk { index, data };
            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message_to(&msg, target);
                }
            });
        }
    }

    // Cleanup stale players
    let stale_entities: Vec<(u64, Option<u32>)> =
        with_state(|s| s.tracker.cleanup_stale_with_entities()).unwrap_or_default();

    for (steam_id, entity_id) in &stale_entities {
        dc_api::crash_log(&format!("[MP] player {} timed out", steam_id));
        if let Some(eid) = entity_id {
            api.destroy_entity(*eid);
        }
    }

    // Entity management (API v9+)
    if api.version() < 9 {
        return;
    }

    let (is_host, join_state) =
        with_state(|s| (s.is_host, s.join_state)).unwrap_or((false, JoinState::Idle));
    if !is_host && join_state != JoinState::Loaded {
        return;
    }

    update_entities(api, dt);

    // Roof safety net if local player is above Y threshold
    roof_safety_check(api, dt);

    // Entity lifecycle UMA retry, collider addition
    entity_lifecycle(api);
}

static mut ROOF_CHECK_TIMER: f32 = 0.0;

fn roof_safety_check(api: &Api, dt: f32) {
    let has_entities = with_state(|s| s.tracker.player_count() > 0).unwrap_or(false);
    if !has_entities {
        return;
    }

    // Safety: single-threaded access from game thread only
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

fn carry_offsets(object_type: u8, _has_hand_bone: bool) -> (Vec3, Vec3) {
    use crate::protocol::object_types;

    match object_type {
        object_types::SERVER_1U => (Vec3::new(0.0, 1.3, 0.35), Vec3::new(0.0, 0.0, 0.0)),
        object_types::SERVER_7U => (Vec3::new(0.0, 1.4, 0.35), Vec3::new(0.0, 0.0, 0.0)),
        object_types::SERVER_3U => (Vec3::new(0.0, 1.35, 0.35), Vec3::new(0.0, 0.0, 0.0)),
        object_types::SWITCH => (Vec3::new(0.0, 1.3, 0.35), Vec3::new(0.0, 0.0, 0.0)),
        object_types::RACK => (Vec3::new(0.0, 1.3, 0.45), Vec3::new(0.0, 0.0, 0.0)),
        object_types::CABLE_SPINNER => (Vec3::new(0.15, 1.1, 0.30), Vec3::new(0.0, 0.0, 0.0)),
        object_types::PATCH_PANEL => (Vec3::new(0.0, 1.3, 0.05), Vec3::new(0.0, 0.0, 0.0)),
        object_types::SFP_MODULE => (Vec3::new(0.15, 1.3, 0.4), Vec3::new(0.0, 0.0, 0.0)),
        object_types::SFP_BOX => (Vec3::new(0.10, 1.3, 0.30), Vec3::new(0.0, 0.0, 0.0)),
        _ => (Vec3::new(0.0, 0.95, 0.35), Vec3::new(0.0, 0.0, 0.0)),
    }
}

struct LifecycleAction {
    steam_id: u64,
    entity_id: u32,
    action: LifecycleKind,
}

enum LifecycleKind {
    UmaRetry,
    AddCollider,
}

fn entity_lifecycle(api: &Api) {
    let now = Instant::now();

    let actions: Vec<LifecycleAction> = with_state(|s| {
        let mut out = Vec::new();

        s.tracker.for_each_player_mut(|player| {
            let eid = match player.entity_id {
                Some(id) => id,
                None => return,
            };

            let is_ready = api.is_entity_ready(eid).unwrap_or(false);

            if !is_ready {
                if let Some(st) = player.spawn_time {
                    if now.duration_since(st).as_secs_f32() > UMA_RETRY_TIMEOUT_SECS {
                        dc_api::crash_log(&format!(
                            "[MP] UMA timeout for entity {} (player {}), will retry",
                            eid, player.steam_id
                        ));
                        out.push(LifecycleAction {
                            steam_id: player.steam_id,
                            entity_id: eid,
                            action: LifecycleKind::UmaRetry,
                        });
                    }
                }
                return;
            }

            if player.uma_ready_time.is_none() {
                player.uma_ready_time = Some(now);
            }

            if !player.collider_added {
                if let Some(ready_time) = player.uma_ready_time {
                    if now.duration_since(ready_time).as_secs_f32() > COLLIDER_DELAY_SECS {
                        if let Some(ep) = api.get_entity_position(eid) {
                            let dx = ep.x - 5.0;
                            let dz = ep.z - (-24.0);
                            let dist_sq = dx * dx + dz * dz;
                            if dist_sq >= COLLIDER_SPAWN_DIST_SQ {
                                out.push(LifecycleAction {
                                    steam_id: player.steam_id,
                                    entity_id: eid,
                                    action: LifecycleKind::AddCollider,
                                });
                            }
                        }
                    }
                }
            }
        });

        out
    })
    .unwrap_or_default();

    for action in actions {
        match action.action {
            LifecycleKind::UmaRetry => {
                api.destroy_entity(action.entity_id);
                with_state(|s| {
                    if let Some(player) = s.tracker.get_player_mut(action.steam_id) {
                        player.entity_id = None;
                        player.spawn_time = None;
                        player.uma_ready_time = None;
                        player.collider_added = false;
                    }
                });
            }
            LifecycleKind::AddCollider => {
                api.add_entity_collider(action.entity_id);
                with_state(|s| {
                    if let Some(player) = s.tracker.get_player_mut(action.steam_id) {
                        player.collider_added = true;
                    }
                });
            }
        }
    }
}

struct SpawnInfo {
    steam_id: u64,
    prefab_idx: u32,
    pos: Vec3,
    rot_y: f32,
    name: String,
}

struct UpdateInfo {
    entity_id: u32,
    pos: Vec3,
    irot: f32,
    speed: f32,
    player_state: PlayerStateSnapshot,
    carry_changed: bool,
    old_carry_type: u8,
}

fn update_entities(api: &Api, _dt: f32) {
    let prefab_count = api.get_prefab_count().unwrap_or(1).max(1);

    let (to_spawn, to_update): (Vec<SpawnInfo>, Vec<UpdateInfo>) = with_state(|s| {
        let mut spawns = Vec::new();
        let mut updates = Vec::new();

        s.tracker.for_each_player_mut(|player| {
            if player.needs_spawn() {
                let pos = if player.use_default_spawn {
                    if let Some(ds) = s.default_spawn {
                        dc_api::crash_log(&format!(
                            "[MP] Using default spawn ({:.1},{:.1},{:.1}) for player {}",
                            ds.x, ds.y, ds.z, player.steam_id
                        ));
                        ds
                    } else {
                        player.pos
                    }
                } else {
                    player.pos
                };

                player.use_default_spawn = false;

                spawns.push(SpawnInfo {
                    steam_id: player.steam_id,
                    prefab_idx: (player.steam_id % prefab_count as u64) as u32,
                    pos,
                    rot_y: player.rot_y,
                    name: player.name.clone(),
                });
            } else if let Some(eid) = player.entity_id {
                let (ix, iy, iz, irot) = player.interpolated_position();
                let dx = player.pos.x - player.prev_pos.x;
                let dz = player.pos.z - player.prev_pos.z;

                let speed = (dx * dx + dz * dz).sqrt() / POSITION_SEND_INTERVAL;

                let carry_changed =
                    player.player_state.object_in_hand != player.last_applied_carry_type;
                let old_carry = player.last_applied_carry_type;
                if carry_changed {
                    player.last_applied_carry_type = player.player_state.object_in_hand;
                }

                updates.push(UpdateInfo {
                    entity_id: eid,
                    pos: Vec3::new(ix, iy, iz),
                    irot,
                    speed,
                    player_state: player.player_state,
                    carry_changed,
                    old_carry_type: old_carry,
                });
            }
        });

        (spawns, updates)
    })
    .unwrap_or_default();

    for info in to_spawn {
        if let Some(eid) = api.spawn_character(info.prefab_idx, info.pos, info.rot_y, &info.name) {
            dc_api::crash_log(&format!(
                "[MP] Spawned entity {} for player {} '{}'",
                eid, info.steam_id, info.name
            ));

            with_state(|s| {
                s.tracker.set_entity_id(info.steam_id, eid);

                if let Some(player) = s.tracker.get_player_mut(info.steam_id) {
                    player.spawn_time = Some(Instant::now());
                    player.uma_ready_time = None;
                    player.collider_added = false;
                }
            });
        }
    }

    for info in to_update {
        api.set_entity_position(info.entity_id, info.pos, info.irot);

        let is_walking = info.speed > 0.1;
        api.set_entity_animation(info.entity_id, info.speed, is_walking);

        if info.carry_changed {
            if info.old_carry_type != 0 {
                api.destroy_entity_carry_visual(info.entity_id);
            }

            let obj_type = info.player_state.object_in_hand;
            if obj_type != 0 {
                api.create_entity_carry_visual(info.entity_id, obj_type as u32);

                // TODO: has_hand_bone is assumed true here could track per entity if needed
                let (pos, rot) = carry_offsets(obj_type, true);
                api.set_entity_carry_transform(info.entity_id, pos, rot);
            }

            api.set_entity_carry_anim(info.entity_id, obj_type != 0);
        }
        api.set_entity_crouching(info.entity_id, info.player_state.is_crouching);
        api.set_entity_sitting(info.entity_id, info.player_state.is_sitting);
    }
}
