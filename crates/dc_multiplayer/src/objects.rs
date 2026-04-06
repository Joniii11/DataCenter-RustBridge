//! Multiplayer aware world object extensions

#![allow(dead_code)]

use dc_api::world::{NetworkSwitch, ObjectHandle, Server, WorldObject};
use dc_api::{Api, Quat, Vec3};

use crate::protocol::WorldAction;

/// Extension of [`WorldObject`] for objects that participate in multiplayer sync.
pub trait SyncedObject: WorldObject {
    /// The `object_type` byte used in [`WorldAction`] protocol messages
    fn wire_type() -> u8;

    /// Build an [`WorldAction::ObjectPickedUp`] message for this object
    fn pickup_action(&self) -> WorldAction {
        WorldAction::ObjectPickedUp {
            object_id: self.id().to_string(),
            object_type: Self::wire_type(),
        }
    }

    /// Build an [`WorldAction::ObjectDropped`] message for this object
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

    /// Find an object of this type by ID and execute a pickup
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

    /// Find an object of this type by ID and execute a drop
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
        crate::protocol::object_types::SERVER_1U
        // NOTE: all server sizes (1U/3U/7U) share the Il2Cpp.Server component
    }
}

impl SyncedObject for NetworkSwitch {
    fn wire_type() -> u8 {
        crate::protocol::object_types::SWITCH
    }
}

/// Try to pick up an object across all known synced types
pub fn dispatch_pickup(api: &Api, object_id: &str) -> bool {
    Server::execute_remote_pickup(api, object_id)
        || NetworkSwitch::execute_remote_pickup(api, object_id)
}

/// Try to drop an object across all known synced types
pub fn dispatch_drop(api: &Api, object_id: &str, pos: Vec3, rot: Quat) -> bool {
    Server::execute_remote_drop(api, object_id, pos, rot)
        || NetworkSwitch::execute_remote_drop(api, object_id, pos, rot)
}

/// Try to find an object handle across all known synced types
pub fn dispatch_find(api: &Api, object_id: &str) -> Option<ObjectHandle> {
    Server::find_by_id(api, object_id)
        .map(|s| s.handle())
        .or_else(|| NetworkSwitch::find_by_id(api, object_id).map(|s| s.handle()))
}
