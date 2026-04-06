use crate::player::PlayerStateSnapshot;
use crate::protocol;
use crate::state::*;
use dc_api::{Api, Vec3};
use std::time::Instant;

const COLLIDER_DELAY_SECS: f32 = 3.0;
const COLLIDER_SPAWN_DIST_SQ: f32 = 4.0;
const UMA_RETRY_TIMEOUT_SECS: f32 = 15.0;

pub(super) fn carry_offsets(object_type: u8, _has_hand_bone: bool) -> (Vec3, Vec3) {
    use protocol::object_types;

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

pub(super) fn entity_lifecycle(api: &Api) {
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

pub(super) fn update_entities(api: &Api, _dt: f32) {
    let prefab_count = api.get_prefab_count().unwrap_or(1).max(1);

    let (to_spawn, to_update): (Vec<SpawnInfo>, Vec<UpdateInfo>) = with_state(|s| {
        let mut spawns = Vec::new();
        let mut updates = Vec::new();

        s.tracker.for_each_player_mut(|player| {
            if player.needs_spawn() {
                let pos = if player.use_default_spawn {
                    if let Some(ds) = s.session.default_spawn {
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

                let (pos, rot) = carry_offsets(obj_type, true);
                api.set_entity_carry_transform(info.entity_id, pos, rot);
            }

            api.set_entity_carry_anim(info.entity_id, obj_type != 0);
        }
        api.set_entity_crouching(info.entity_id, info.player_state.is_crouching);
        api.set_entity_sitting(info.entity_id, info.player_state.is_sitting);
    }
}
