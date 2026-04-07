use crate::Api;

use super::{ObjectHandle, ObjectType, StringField, WorldObject};

#[derive(Debug, Clone)]
pub struct PatchPanel {
    handle: ObjectHandle,
    id: String,
}

impl WorldObject for PatchPanel {
    const OBJECT_TYPE: ObjectType = ObjectType::PATCH_PANEL;
    const ID_FIELD: StringField = StringField::PATCH_PANEL_ID;

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

impl PatchPanel {
    /// The stable `patchPanelId`
    #[inline]
    pub fn patch_panel_id(&self) -> &str {
        self.id()
    }

    /// Read the Unity `gameObject.name`
    pub fn game_object_name(&self, api: &Api) -> String {
        api.obj_get_string_field(self.handle, StringField::GAME_OBJECT_NAME)
    }

    /// Convenience: find by ID, deactivate
    pub fn pickup_by_id(api: &Api, patch_panel_id: &str) -> bool {
        Self::find_by_id(api, patch_panel_id)
            .map(|pp| pp.pickup(api))
            .unwrap_or(false)
    }

    /// Convenience: find by ID, reactivate at position with physics
    pub fn drop_by_id(api: &Api, patch_panel_id: &str, pos: crate::Vec3, rot: crate::Quat) -> bool {
        Self::find_by_id(api, patch_panel_id)
            .map(|pp| pp.drop_at(api, pos, rot))
            .unwrap_or(false)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn type_constants() {
        assert_eq!(PatchPanel::OBJECT_TYPE, ObjectType::PATCH_PANEL);
        assert_eq!(PatchPanel::OBJECT_TYPE.0, 7);
        assert_eq!(PatchPanel::ID_FIELD, StringField::PATCH_PANEL_ID);
        assert_eq!(PatchPanel::ID_FIELD.0, 4);
    }

    #[test]
    fn construction_and_accessors() {
        let pp = PatchPanel::from_handle(ObjectHandle(0xABC), "pp-42".into());
        assert_eq!(pp.handle(), ObjectHandle(0xABC));
        assert_eq!(pp.id(), "pp-42");
        assert_eq!(pp.patch_panel_id(), "pp-42");
    }

    #[test]
    fn handle_validity() {
        let valid = PatchPanel::from_handle(ObjectHandle(1), "x".into());
        let invalid = PatchPanel::from_handle(ObjectHandle::INVALID, "".into());
        assert!(valid.handle().is_valid());
        assert!(!invalid.handle().is_valid());
    }
}
