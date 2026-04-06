use crate::objects;
use crate::protocol;
use crate::state::*;
use dc_api::{Api, Quat, Vec3};

pub(super) fn detect_carry_transitions(api: &Api, cur_num: u8) {
    let (connected, loaded, executing) = match with_state(|s| {
        (
            s.session.connected,
            s.session.is_host || s.session.join_state == JoinState::Loaded,
            s.executing_remote_action,
        )
    }) {
        Some(t) => t,
        None => return,
    };
    if !connected || !loaded || executing {
        with_state(|s| s.carry.prev_count = cur_num);
        return;
    }

    let prev_num = with_state(|s| {
        let p = s.carry.prev_count;
        s.carry.prev_count = cur_num;
        p
    })
    .unwrap_or(0);

    if prev_num == cur_num {
        return;
    }

    if prev_num == 0 && cur_num > 0 {
        let (id, obj_type) = api.get_held_object();
        if id.is_empty() {
            dc_api::crash_log("[CARRY] pickup transition but get_held_object returned empty");
            return;
        }

        with_state(|s| {
            s.carry.held_id = id.clone();
            s.carry.held_type = obj_type;
        });

        dc_api::crash_log(&format!(
            "[CARRY] pickup detected: '{}' type={}",
            id, obj_type
        ));

        let action = protocol::WorldAction::ObjectPickedUp {
            object_id: id,
            object_type: obj_type,
        };
        crate::send_world_action(api, action);
    }

    if prev_num > 0 && cur_num == 0 {
        let suppress = with_state(|s| {
            if s.carry.suppress_next_drop {
                s.carry.suppress_next_drop = false;
                true
            } else {
                false
            }
        })
        .unwrap_or(false);

        if suppress {
            dc_api::crash_log("[CARRY] drop suppressed (rack install)");
            with_state(|s| {
                s.carry.held_id.clear();
                s.carry.held_type = 0;
            });
            return;
        }

        let (id, obj_type) = with_state(|s| {
            let r = (s.carry.held_id.clone(), s.carry.held_type);
            s.carry.held_id.clear();
            s.carry.held_type = 0;
            r
        })
        .unwrap_or_default();

        if id.is_empty() {
            dc_api::crash_log("[CARRY] drop transition but no held object stored");
            return;
        }

        let (pos, rot) = match objects::dispatch_find(api, &id) {
            Some(handle) => {
                let p = api.obj_get_position(handle);
                let r = api.obj_get_rotation(handle);
                (p, r)
            }
            None => {
                if let Some((px, py, pz, _ry)) = api.get_player_position() {
                    (Vec3::new(px, py, pz), Quat::identity())
                } else {
                    (Vec3::zero(), Quat::identity())
                }
            }
        };

        dc_api::crash_log(&format!(
            "[CARRY] drop detected: '{}' type={} pos=({:.1},{:.1},{:.1})",
            id, obj_type, pos.x, pos.y, pos.z
        ));

        let action = protocol::WorldAction::ObjectDropped {
            object_id: id,
            object_type: obj_type,
            pos_x: pos.x,
            pos_y: pos.y,
            pos_z: pos.z,
            rot_x: rot.x,
            rot_y: rot.y,
            rot_z: rot.z,
            rot_w: rot.w,
        };

        crate::send_world_action(api, action);
    }
}
