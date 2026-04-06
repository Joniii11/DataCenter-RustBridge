//! Server game object [`WorldObject`] implementation.

use super::{ObjectHandle, ObjectType, StringField, WorldObject};
use crate::Api;

/// A server object (1U, 3U, or 7U) in the game world
#[derive(Debug, Clone)]
pub struct Server {
    handle: ObjectHandle,
    id: String,
}

impl WorldObject for Server {
    const OBJECT_TYPE: ObjectType = ObjectType::SERVER;
    const ID_FIELD: StringField = StringField::SERVER_ID;

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

impl Server {
    /// The stable `Server.ServerID`
    pub fn server_id(&self) -> &str {
        &self.id
    }

    /// Read `Server.rackPositionUID` (the rack slot this server is installed in)
    pub fn rack_position_uid(&self, api: &Api) -> String {
        api.obj_get_string_field(self.handle, StringField::RACK_POSITION_UID)
    }

    /// Read the Unity `gameObject.name`
    pub fn name(&self, api: &Api) -> String {
        api.obj_get_string_field(self.handle, StringField::GAME_OBJECT_NAME)
    }

    /// Check if this server is currently installed in a rack
    pub fn is_in_rack(&self, api: &Api) -> bool {
        let uid = self.rack_position_uid(api);
        !uid.is_empty() && uid != "0"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn server_type_constants() {
        assert_eq!(Server::OBJECT_TYPE, ObjectType::SERVER);
        assert_eq!(Server::ID_FIELD, StringField::SERVER_ID);
        assert_eq!(Server::OBJECT_TYPE.0, 0);
        assert_eq!(Server::ID_FIELD.0, 0);
    }

    #[test]
    fn server_construction() {
        let srv = Server::from_handle(ObjectHandle(0xCAFE), "srv-001".into());
        assert_eq!(srv.handle(), ObjectHandle(0xCAFE));
        assert_eq!(srv.id(), "srv-001");
        assert_eq!(srv.server_id(), "srv-001");
    }

    #[test]
    fn server_handle_validity() {
        let valid = Server::from_handle(ObjectHandle(1), "a".into());
        let invalid = Server::from_handle(ObjectHandle::INVALID, "b".into());
        assert!(valid.handle().is_valid());
        assert!(!invalid.handle().is_valid());
    }

    #[test]
    fn server_clone() {
        let srv = Server::from_handle(ObjectHandle(42), "clone-me".into());
        let cloned = srv.clone();
        assert_eq!(srv.handle(), cloned.handle());
        assert_eq!(srv.id(), cloned.id());
    }
}
