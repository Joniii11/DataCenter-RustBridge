#![allow(dead_code)]

use dc_api::world::{NetworkSwitch, ObjectHandle, Server, WorldObject};
use dc_api::{Api, Quat, Vec3};

use crate::protocol::WorldAction;

pub trait SyncedObject: WorldObject {
    /// The `object_type` byte used in [`WorldAction`] protocol messages
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
            false
        }
    }
}

impl SyncedObject for Server {
    fn wire_type() -> u8 {
        // All server sizes (1U/3U/7U) share the Il2Cpp.Server component
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
