mod network_switch;
mod patch_panel;
mod server;

pub mod registry;
pub use registry::ObjectIdRegistry;

pub use network_switch::NetworkSwitch;
pub use patch_panel::PatchPanel;
pub use server::Server;

use crate::{Api, Quat, Vec3};

/// Opaque handle to a Unity/Il2Cpp game object.
/// Valid only for the current frame / operation do **not** cache across frames.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ObjectHandle(pub u64);

impl ObjectHandle {
    pub const INVALID: Self = Self(0);

    /// Returns `true` if this is non zero
    #[inline]
    pub fn is_valid(self) -> bool {
        self.0 != 0
    }
}

impl From<u64> for ObjectHandle {
    fn from(v: u64) -> Self {
        Self(v)
    }
}

impl From<ObjectHandle> for u64 {
    fn from(h: ObjectHandle) -> Self {
        h.0
    }
}

/// Object type ID sent to C#'s `ObjFindByTypeImpl` switch.
///
/// This is a **newtype** so any crate can define additional constants without modifying `dc_api`
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ObjectType(pub u8);

impl ObjectType {
    /// Server (1U, 3U, 7U
    pub const SERVER: Self = Self(0);
    /// Network switch
    pub const NETWORK_SWITCH: Self = Self(4);
    /// Patch panel
    pub const PATCH_PANEL: Self = Self(7);
}

/// String field ID sent to C#'s `ObjGetStringFieldImpl` switch.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct StringField(pub u16);

impl StringField {
    /// Server.ServerID
    pub const SERVER_ID: Self = Self(0);
    /// NetworkSwitch.SwitchId
    pub const SWITCH_ID: Self = Self(1);
    /// Server.RackPositionUID
    pub const RACK_POSITION_UID: Self = Self(2);
    /// GameObject.Name
    pub const GAME_OBJECT_NAME: Self = Self(3);
    /// PatchPanel.PatchPanelId
    pub const PATCH_PANEL_ID: Self = Self(4);
}

/// Trait for game world object types.
///
/// Implement this on your own struct to get **all** query, mutation,
/// physics, and composite operations for free.  You only need to provide
/// five items:
///
/// | Item | Purpose |
/// |------|---------|
/// | [`OBJECT_TYPE`](Self::OBJECT_TYPE) | which C# type to search for |
/// | [`ID_FIELD`](Self::ID_FIELD) | which field holds the stable ID |
/// | [`from_handle`](Self::from_handle) | construct your struct |
/// | [`handle`](Self::handle) | return the opaque handle |
/// | [`id`](Self::id) | return the cached ID string |
pub trait WorldObject: Sized {
    /// The object type constant used for `obj_find_by_type`
    const OBJECT_TYPE: ObjectType;

    /// Which string field holds this types stable ID
    const ID_FIELD: StringField;

    /// Construct from a handle and a prefetched ID string
    fn from_handle(handle: ObjectHandle, id: String) -> Self;

    /// Get the underlying handle
    fn handle(&self) -> ObjectHandle;

    /// Get the cached stable ID
    fn id(&self) -> &str;

    /// Find all objects of this type currently in the scene
    fn find_all(api: &Api) -> Vec<Self> {
        let handles = api.obj_find_by_type(Self::OBJECT_TYPE);
        handles
            .into_iter()
            .map(|h| {
                let id = api.obj_get_string_field(h, Self::ID_FIELD);
                Self::from_handle(h, id)
            })
            .collect()
    }

    /// Find a specific object by its stable ID
    fn find_by_id(api: &Api, target_id: &str) -> Option<Self> {
        let handle = api.obj_find_by_id(Self::OBJECT_TYPE, Self::ID_FIELD, target_id);
        if handle.is_valid() {
            Some(Self::from_handle(handle, target_id.to_string()))
        } else {
            None
        }
    }

    /// Check if the GameObject is active
    fn is_active(&self, api: &Api) -> bool {
        api.obj_is_active(self.handle())
    }

    /// Sets the active state
    fn set_active(&self, api: &Api, active: bool) -> bool {
        api.obj_set_active(self.handle(), active)
    }

    /// Get the world position
    fn position(&self, api: &Api) -> Vec3 {
        api.obj_get_position(self.handle())
    }

    /// Get the world rotation
    fn rotation(&self, api: &Api) -> crate::Quat {
        api.obj_get_rotation(self.handle())
    }

    /// Set the world position
    fn set_position(&self, api: &Api, pos: Vec3) -> bool {
        api.obj_set_position(self.handle(), pos)
    }

    /// Set the rotation
    fn set_rotation(&self, api: &Api, rot: Quat) -> bool {
        api.obj_set_rotation(self.handle(), rot)
    }

    /// reparent
    fn reparent_to_world(&self, api: &Api) -> bool {
        api.obj_set_parent_to_world(self.handle())
    }

    /// Read any string field by ID. Useful for fields not covered
    fn get_string_field(&self, api: &Api, field: StringField) -> String {
        api.obj_get_string_field(self.handle(), field)
    }

    /// Read the Unity
    fn game_object_name(&self, api: &Api) -> String {
        self.get_string_field(api, StringField::GAME_OBJECT_NAME)
    }

    /// Set the kinematic state
    fn set_kinematic(&self, api: &Api, kinematic: bool) -> bool {
        api.rb_set_kinematic(self.handle(), kinematic)
    }

    /// `rigidbody.useGravity
    fn set_gravity(&self, api: &Api, use_gravity: bool) -> bool {
        api.rb_set_gravity(self.handle(), use_gravity)
    }

    /// `rigidbody.WakeUp()`
    fn wake_up(&self, api: &Api) -> bool {
        api.rb_wake_up(self.handle())
    }

    /// Pickup: deactivate the object
    fn pickup(&self, api: &Api) -> bool {
        self.set_active(api, false)
    }

    fn drop_at(&self, api: &Api, pos: Vec3, rot: Quat) -> bool {
        api.world_drop_object(self.id(), pos, rot.x, rot.y, rot.z, rot.w)
    }

    /// Enable physics
    fn enable_physics(&self, api: &Api) {
        self.set_kinematic(api, false);
        self.set_gravity(api, true);
        self.wake_up(api);
    }

    /// Disable physics
    fn disable_physics(&self, api: &Api) {
        self.set_kinematic(api, true);
        self.set_gravity(api, false);
    }

    /// Parent this objects transform to `parent`
    fn set_parent(&self, api: &Api, parent: ObjectHandle) -> bool {
        api.obj_set_parent(self.handle(), parent)
    }

    /// Set the local position
    fn set_local_position(&self, api: &Api, pos: Vec3) -> bool {
        api.obj_set_local_position(self.handle(), pos)
    }

    /// Set the local rotation
    fn set_local_rotation(&self, api: &Api, rot: Quat) -> bool {
        api.obj_set_local_rotation(self.handle(), rot)
    }

    /// Install this object in a rack slot identified by `rack_position_uid`
    fn install_in_rack(&self, api: &Api, rack_position_uid: i32) -> bool {
        install_in_rack(api, self.handle(), rack_position_uid, Self::OBJECT_TYPE.0)
    }

    /// Remove this object from its current rack slot
    fn remove_from_rack(&self, api: &Api) -> bool {
        remove_from_rack(api, self.handle(), Self::OBJECT_TYPE.0)
    }
}

pub fn install_in_rack(
    api: &Api,
    handle: ObjectHandle,
    rack_position_uid: i32,
    object_type: u8,
) -> bool {
    let rack_pos = api.rack_find_position(rack_position_uid);
    if !rack_pos.is_valid() {
        return false;
    }

    if !api.obj_is_active(handle) {
        api.obj_set_active(handle, true);
    }

    if !api.rack_game_install(handle, rack_pos, object_type) {
        return false;
    }

    api.obj_set_parent(handle, rack_pos);
    api.obj_set_local_position(handle, Vec3::zero());
    api.obj_set_local_rotation(handle, Quat::identity());
    api.rb_set_kinematic(handle, true);
    api.rb_set_gravity(handle, false);
    true
}

/// Remove an object by handle from rack slot
pub fn remove_from_rack(api: &Api, handle: ObjectHandle, object_type: u8) -> bool {
    api.rack_game_uninstall(handle, object_type);
    api.obj_set_parent_to_world(handle);
    api.rb_set_kinematic(handle, false);
    api.rb_set_gravity(handle, true);
    api.rb_wake_up(handle);
    true
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::Quat;

    struct CustomGizmo {
        handle: ObjectHandle,
        id: String,
    }

    impl WorldObject for CustomGizmo {
        const OBJECT_TYPE: ObjectType = ObjectType(99);
        const ID_FIELD: StringField = StringField(99);

        fn from_handle(handle: ObjectHandle, id: String) -> Self {
            Self { handle, id }
        }
        fn handle(&self) -> ObjectHandle {
            self.handle
        }
        fn id(&self) -> &str {
            &self.id
        }
    }

    #[test]
    fn handle_validity() {
        assert!(!ObjectHandle::INVALID.is_valid());
        assert!(ObjectHandle(1).is_valid());
        assert!(ObjectHandle(0xDEAD_BEEF).is_valid());
    }

    #[test]
    fn handle_conversions() {
        let h: ObjectHandle = 42u64.into();
        assert_eq!(h.0, 42);
        let v: u64 = h.into();
        assert_eq!(v, 42);
    }

    #[test]
    fn builtin_object_type_constants() {
        assert_eq!(ObjectType::SERVER.0, 0);
        assert_eq!(ObjectType::NETWORK_SWITCH.0, 4);
    }

    #[test]
    fn builtin_string_field_constants() {
        assert_eq!(StringField::SERVER_ID.0, 0);
        assert_eq!(StringField::SWITCH_ID.0, 1);
        assert_eq!(StringField::RACK_POSITION_UID.0, 2);
        assert_eq!(StringField::GAME_OBJECT_NAME.0, 3);
    }

    #[test]
    fn custom_constants_compile() {
        let t = ObjectType(42);
        let f = StringField(100);
        assert_eq!(t.0, 42);
        assert_eq!(f.0, 100);
    }

    #[test]
    fn custom_world_object() {
        let g = CustomGizmo::from_handle(ObjectHandle(0xCAFE), "gizmo-1".into());
        assert_eq!(g.handle(), ObjectHandle(0xCAFE));
        assert_eq!(g.id(), "gizmo-1");
        assert_eq!(CustomGizmo::OBJECT_TYPE.0, 99);
        assert_eq!(CustomGizmo::ID_FIELD.0, 99);
    }

    #[test]
    fn builtin_server_type() {
        let srv = Server::from_handle(ObjectHandle(1), "srv-1".into());
        assert_eq!(srv.handle(), ObjectHandle(1));
        assert_eq!(srv.id(), "srv-1");
        assert_eq!(Server::OBJECT_TYPE, ObjectType::SERVER);
        assert_eq!(Server::ID_FIELD, StringField::SERVER_ID);
    }

    #[test]
    fn builtin_switch_type() {
        let sw = NetworkSwitch::from_handle(ObjectHandle(2), "sw-1".into());
        assert_eq!(sw.handle(), ObjectHandle(2));
        assert_eq!(sw.id(), "sw-1");
        assert_eq!(NetworkSwitch::OBJECT_TYPE, ObjectType::NETWORK_SWITCH);
        assert_eq!(NetworkSwitch::ID_FIELD, StringField::SWITCH_ID);
    }

    #[test]
    fn quat_identity() {
        let q = Quat::identity();
        assert_eq!((q.x, q.y, q.z, q.w), (0.0, 0.0, 0.0, 1.0));
    }
}
