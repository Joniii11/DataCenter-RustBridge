use crate::Api;

use super::{ObjectHandle, ObjectType, StringField, WorldObject};

#[derive(Debug, Clone)]
pub struct NetworkSwitch {
    handle: ObjectHandle,
    id: String,
}

impl WorldObject for NetworkSwitch {
    const OBJECT_TYPE: ObjectType = ObjectType::NETWORK_SWITCH;
    const ID_FIELD: StringField = StringField::SWITCH_ID;

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

impl NetworkSwitch {
    /// The stable `switchId`
    #[inline]
    pub fn switch_id(&self) -> &str {
        self.id()
    }

    /// Read the Unity `gameObject.name`
    pub fn game_object_name(&self, api: &Api) -> String {
        api.obj_get_string_field(self.handle, StringField::GAME_OBJECT_NAME)
    }

    /// Convenience: find by ID, deactivate
    pub fn pickup_by_id(api: &Api, switch_id: &str) -> bool {
        Self::find_by_id(api, switch_id)
            .map(|sw| sw.pickup(api))
            .unwrap_or(false)
    }

    /// Convenience: find by ID, reactivate at position with physics
    pub fn drop_by_id(api: &Api, switch_id: &str, pos: crate::Vec3, rot: crate::Quat) -> bool {
        Self::find_by_id(api, switch_id)
            .map(|sw| sw.drop_at(api, pos, rot))
            .unwrap_or(false)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn type_constants() {
        assert_eq!(NetworkSwitch::OBJECT_TYPE, ObjectType::NETWORK_SWITCH);
        assert_eq!(NetworkSwitch::OBJECT_TYPE.0, 4);
        assert_eq!(NetworkSwitch::ID_FIELD, StringField::SWITCH_ID);
        assert_eq!(NetworkSwitch::ID_FIELD.0, 1);
    }

    #[test]
    fn construction_and_accessors() {
        let sw = NetworkSwitch::from_handle(ObjectHandle(0xABC), "sw-42".into());
        assert_eq!(sw.handle(), ObjectHandle(0xABC));
        assert_eq!(sw.id(), "sw-42");
        assert_eq!(sw.switch_id(), "sw-42");
    }

    #[test]
    fn handle_validity() {
        let valid = NetworkSwitch::from_handle(ObjectHandle(1), "x".into());
        let invalid = NetworkSwitch::from_handle(ObjectHandle::INVALID, "".into());
        assert!(valid.handle().is_valid());
        assert!(!invalid.handle().is_valid());
    }
}
