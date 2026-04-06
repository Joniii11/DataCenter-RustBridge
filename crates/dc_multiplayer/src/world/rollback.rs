use crate::objects;
use crate::state::with_state;
use dc_api::{Quat, Vec3};

#[derive(Debug, Clone)]
#[allow(dead_code)]
pub enum RollbackInfo {
    UndoPickup {
        object_id: String,
        object_type: u8,
        original_pos: (f32, f32, f32),
        original_rot: (f32, f32, f32, f32),
    },
    UndoDrop {
        object_id: String,
    },
    UndoInstall {
        object_id: String,
        object_type: u8,
        previous_pos: (f32, f32, f32),
        previous_rot: (f32, f32, f32, f32),
    },
    UndoRemoveFromRack {
        object_id: String,
        object_type: u8,
        rack_position_uid: i32,
    },
    UndoPowerToggle {
        object_id: String,
        was_on: bool,
    },
    UndoPropertyChange {
        object_id: String,
        key: String,
        old_value: String,
    },
    UndoCableConnect {
        cable_id: i32,
    },
    UndoCableDisconnect {
        cable_id: i32,
        start_type: u8,
        start_pos: (f32, f32, f32),
        start_device_id: String,
        end_type: u8,
        end_pos: (f32, f32, f32),
        end_device_id: String,
    },
    UndoSpawn {
        object_id: String,
    },
    UndoDestroy {
        object_id: String,
        object_type: u8,
        prefab_id: i32,
        pos: (f32, f32, f32),
        rot: (f32, f32, f32, f32),
    },
    None,
}

pub fn execute_rollback(api: &dc_api::Api, rollback: &RollbackInfo) {
    with_state(|s| s.executing_remote_action = true);

    execute_rollback_inner(api, rollback);

    with_state(|s| s.executing_remote_action = false);
}

fn execute_rollback_inner(api: &dc_api::Api, rollback: &RollbackInfo) {
    match rollback {
        RollbackInfo::UndoPickup {
            object_id,
            original_pos,
            original_rot,
            ..
        } => {
            let pos: Vec3 = original_pos.into();
            let rot: Quat = original_rot.into();
            let ok = objects::dispatch_drop(api, object_id, pos, rot);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback pickup → drop '{}' at ({:.1},{:.1},{:.1}) → {}",
                object_id, pos.x, pos.y, pos.z, ok
            ));
        }
        RollbackInfo::UndoDrop { object_id } => {
            let ok = objects::dispatch_pickup(api, object_id);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback drop → pickup '{}' → {}",
                object_id, ok
            ));
        }
        RollbackInfo::UndoInstall {
            object_id,
            previous_pos,
            previous_rot,
            ..
        } => {
            let removed = api.world_remove_from_rack(object_id);
            let pos: Vec3 = previous_pos.into();
            let (rx, ry, rz, rw) = *previous_rot;
            let dropped = api.world_drop_object(object_id, pos, rx, ry, rz, rw);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback install '{}' → remove={} drop={}",
                object_id, removed, dropped
            ));
        }
        RollbackInfo::UndoRemoveFromRack {
            object_id,
            rack_position_uid,
            ..
        } => {
            let ok = api.world_place_in_rack(object_id, *rack_position_uid);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback remove → reinstall '{}' uid={} → {}",
                object_id, rack_position_uid, ok
            ));
        }
        RollbackInfo::UndoPowerToggle { object_id, was_on } => {
            let ok = api.world_set_power(object_id, *was_on);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback power toggle '{}' → was_on={} → {}",
                object_id, was_on, ok
            ));
        }
        RollbackInfo::UndoPropertyChange {
            object_id,
            key,
            old_value,
        } => {
            let ok = api.world_set_property(object_id, key, old_value);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback property '{}' {}={} → {}",
                object_id, key, old_value, ok
            ));
        }
        RollbackInfo::UndoCableConnect { cable_id } => {
            let ok = api.world_disconnect_cable(*cable_id);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback cable connect → disconnect id={} → {}",
                cable_id, ok
            ));
        }
        RollbackInfo::UndoCableDisconnect {
            cable_id,
            start_type,
            start_pos,
            start_device_id,
            end_type,
            end_pos,
            end_device_id,
        } => {
            let spos = start_pos.into();
            let epos = end_pos.into();
            let ok = api.world_connect_cable(
                *cable_id,
                *start_type,
                spos,
                start_device_id,
                *end_type,
                epos,
                end_device_id,
            );
            dc_api::crash_log(&format!(
                "[WORLD] Rollback cable disconnect → reconnect id={} → {}",
                cable_id, ok
            ));
        }
        RollbackInfo::UndoSpawn { object_id } => {
            let ok = api.world_destroy_object(object_id);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback spawn → destroy '{}' → {}",
                object_id, ok
            ));
        }
        RollbackInfo::UndoDestroy {
            object_type,
            prefab_id,
            pos,
            rot,
            ..
        } => {
            let (x, y, z) = *pos;
            let (rx, ry, rz, rw) = *rot;
            let result = api.world_spawn_object(*object_type, *prefab_id, x, y, z, rx, ry, rz, rw);
            dc_api::crash_log(&format!(
                "[WORLD] Rollback destroy → respawn type={} → {:?}",
                object_type, result
            ));
        }
        RollbackInfo::None => {
            dc_api::crash_log("[WORLD] Rollback: no-op (RollbackInfo::None)");
        }
    }
}
