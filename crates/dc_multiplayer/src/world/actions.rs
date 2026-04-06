use crate::objects;
use crate::protocol::WorldAction;
use crate::state::with_state;
use dc_api::{Quat, Vec3};

pub fn execute_world_action(api: &dc_api::Api, action: &WorldAction) {
    with_state(|s| s.executing_remote_action = true);

    execute_world_action_inner(api, action);

    with_state(|s| s.executing_remote_action = false);
}

fn execute_world_action_inner(api: &dc_api::Api, action: &WorldAction) {
    match action {
        WorldAction::ObjectPickedUp { object_id, .. } => {
            let ok = objects::dispatch_pickup(api, object_id);
            if !ok {
                dc_api::crash_log(&format!(
                    "[WORLD] pickup '{}' not found in any type",
                    object_id
                ));
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
            let pos = Vec3::new(*pos_x, *pos_y, *pos_z);
            let rot = Quat::new(*rot_x, *rot_y, *rot_z, *rot_w);
            let ok = objects::dispatch_drop(api, object_id, pos, rot);
            if !ok {
                dc_api::crash_log(&format!(
                    "[WORLD] drop '{}' not found in any type",
                    object_id
                ));
            }
        }
        WorldAction::InstalledInRack {
            object_id,
            rack_position_uid,
            ..
        } => {
            let ok = api.world_place_in_rack(object_id, *rack_position_uid);
            dc_api::crash_log(&format!(
                "[WORLD] Execute install '{}' in rack uid={} → {}",
                object_id, rack_position_uid, ok
            ));
        }
        WorldAction::RemovedFromRack { object_id, .. } => {
            let ok = api.world_remove_from_rack(object_id);
            dc_api::crash_log(&format!(
                "[WORLD] Execute remove from rack '{}' → {}",
                object_id, ok
            ));
        }
        WorldAction::PowerToggled { object_id, is_on } => {
            let ok = api.world_set_power(object_id, *is_on);
            dc_api::crash_log(&format!(
                "[WORLD] Execute power toggle '{}' on={} → {}",
                object_id, is_on, ok
            ));
        }
        WorldAction::PropertyChanged {
            object_id,
            key,
            value,
        } => {
            let ok = api.world_set_property(object_id, key, value);
            dc_api::crash_log(&format!(
                "[WORLD] Execute property '{}' {}={} → {}",
                object_id, key, value, ok
            ));
        }
        WorldAction::CableConnected {
            cable_id,
            start_type,
            start_pos_x,
            start_pos_y,
            start_pos_z,
            start_device_id,
            end_type,
            end_pos_x,
            end_pos_y,
            end_pos_z,
            end_device_id,
        } => {
            let spos = (start_pos_x, start_pos_y, start_pos_z).into();
            let epos = (end_pos_x, end_pos_y, end_pos_z).into();

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
                "[WORLD] Execute cable connect id={} → {}",
                cable_id, ok
            ));
        }
        WorldAction::CableDisconnected { cable_id } => {
            let ok = api.world_disconnect_cable(*cable_id);
            dc_api::crash_log(&format!(
                "[WORLD] Execute cable disconnect id={} → {}",
                cable_id, ok
            ));
        }
        WorldAction::ObjectSpawned {
            object_id,
            object_type,
            prefab_id,
            pos_x,
            pos_y,
            pos_z,
            rot_x,
            rot_y,
            rot_z,
            rot_w,
        } => {
            let result = api.world_spawn_object_with_id(
                object_id,
                *object_type,
                *prefab_id,
                *pos_x,
                *pos_y,
                *pos_z,
                *rot_x,
                *rot_y,
                *rot_z,
                *rot_w,
            );
            dc_api::crash_log(&format!(
                "[WORLD] Execute spawn '{}' type={} prefab={} → {:?}",
                object_id, object_type, prefab_id, result
            ));
        }
        WorldAction::ObjectDestroyed { object_id, .. } => {
            let ok = api.world_destroy_object(object_id);
            dc_api::crash_log(&format!("[WORLD] Execute destroy '{}' → {}", object_id, ok));
        }
    }
}
