#![allow(dead_code)]

use dc_api::world::{NetworkSwitch, ObjectHandle, Server, StringField, WorldObject};
use dc_api::{Api, Quat, Vec3};

use crate::protocol::WorldAction;

pub trait SyncedObject: WorldObject {
    /// The `object_type` byte used in [`WorldAction`] protocol messages.
    fn wire_type() -> u8;

    fn pickup_action(&self) -> WorldAction {
        WorldAction::ObjectPickedUp {
            object_id: self.id().to_string(),
            object_type: Self::wire_type(),
        }
    }

    fn drop_action(&self, pos: Vec3, rot: Quat) -> WorldAction {
        WorldAction::ObjectDropped {
            object_id: self.id().to_string(),
            object_type: Self::wire_type(),
            pos_x: pos.x,
            pos_y: pos.y,
            pos_z: pos.z,
            rot_x: rot.x,
            rot_y: rot.y,
            rot_z: rot.z,
            rot_w: rot.w,
        }
    }

    fn install_in_rack_action(&self, rack_position_uid: i32) -> WorldAction {
        WorldAction::InstalledInRack {
            object_id: self.id().to_string(),
            object_type: Self::wire_type(),
            rack_position_uid,
        }
    }

    fn remove_from_rack_action(&self) -> WorldAction {
        WorldAction::RemovedFromRack {
            object_id: self.id().to_string(),
            object_type: Self::wire_type(),
        }
    }

    fn execute_remote_pickup(api: &Api, object_id: &str) -> bool {
        if let Some(obj) = Self::find_by_id(api, object_id) {
            let ok = obj.pickup(api);
            dc_api::crash_log(&format!(
                "[WORLD] pickup '{}' (type={}) → {}",
                object_id,
                Self::wire_type(),
                ok
            ));
            ok
        } else {
            dc_api::crash_log(&format!(
                "[WORLD] pickup '{}' not found as type={}",
                object_id,
                Self::wire_type()
            ));
            false
        }
    }

    fn execute_remote_drop(api: &Api, object_id: &str, pos: Vec3, rot: Quat) -> bool {
        if let Some(obj) = Self::find_by_id(api, object_id) {
            let ok = obj.drop_at(api, pos, rot);
            dc_api::crash_log(&format!(
                "[WORLD] drop '{}' (type={}) at ({:.1},{:.1},{:.1}) → {}",
                object_id,
                Self::wire_type(),
                pos.x,
                pos.y,
                pos.z,
                ok
            ));
            ok
        } else {
            dc_api::crash_log(&format!(
                "[WORLD] drop '{}' not found as type={}",
                object_id,
                Self::wire_type()
            ));
            false
        }
    }
}

impl SyncedObject for Server {
    fn wire_type() -> u8 {
        crate::protocol::object_types::SERVER_1U
    }
}

impl SyncedObject for NetworkSwitch {
    fn wire_type() -> u8 {
        crate::protocol::object_types::SWITCH
    }
}

pub fn dispatch_pickup(api: &Api, object_id: &str) -> bool {
    Server::execute_remote_pickup(api, object_id)
        || NetworkSwitch::execute_remote_pickup(api, object_id)
}

pub fn dispatch_drop(api: &Api, object_id: &str, pos: Vec3, rot: Quat) -> bool {
    Server::execute_remote_drop(api, object_id, pos, rot)
        || NetworkSwitch::execute_remote_drop(api, object_id, pos, rot)
}

pub fn dispatch_find(api: &Api, object_id: &str) -> Option<ObjectHandle> {
    Server::find_by_id(api, object_id)
        .map(|s| s.handle())
        .or_else(|| NetworkSwitch::find_by_id(api, object_id).map(|s| s.handle()))
}

pub fn dispatch_install_in_rack_action(
    api: &Api,
    object_id: &str,
    object_type: u8,
) -> Option<WorldAction> {
    let handle = dispatch_find(api, object_id)?;
    let uid = api
        .obj_get_string_field(handle, StringField::RACK_POSITION_UID)
        .parse::<i32>()
        .ok()?;

    Some(WorldAction::InstalledInRack {
        object_id: object_id.to_string(),
        object_type,
        rack_position_uid: uid,
    })
}

pub fn dispatch_remote_put_in_rack(
    api: &Api,
    object_id: &str,
    rack_position_uid: i32,
    object_type: u8,
) -> bool {
    if let Some(handle) = dispatch_find(api, object_id) {
        let ok = dc_api::world::install_in_rack(api, handle, rack_position_uid, object_type);

        dc_api::crash_log(&format!(
            "[WORLD] remote install '{object_id}' uid={rack_position_uid} → {ok}"
        ));

        return ok;
    }
    false
}

pub fn dispatch_remote_put_out_of_rack(api: &Api, object_id: &str, object_type: u8) -> bool {
    if let Some(handle) = dispatch_find(api, object_id) {
        let ok = dc_api::world::remove_from_rack(api, handle, object_type);

        dc_api::crash_log(&format!(
            "[WORLD] remote remove '{object_id}' from rack → {ok}"
        ));

        return ok;
    }
    false
}
