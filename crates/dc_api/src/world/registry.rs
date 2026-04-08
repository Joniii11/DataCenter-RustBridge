//! Stable‑ID registry for game objects.
//!
//! When the game creates object clones (e.g. during rack installation), Unity
//! assigns a new instance ID, breaking multiplayer sync. This registry
//! maintains a stable mapping so every side can refer to the same logical
//! object by a persistent string key.
//!
//! The registry lives in `dc_api` (not in a specific mod) so **every** mod
//! — regardless of language — can query or mutate it through the public API.

use std::collections::HashMap;
use std::sync::Mutex;

use crate::world::{ObjectHandle, WorldObject};
use crate::Api;

// ── Global singleton ───────────────────────────────────────────────────────

static REGISTRY: Mutex<Option<ObjectIdRegistry>> = Mutex::new(None);

/// Run a closure with shared read access to the global registry.
/// Returns `None` if the registry has not been initialised yet.
pub fn with_registry<F, R>(f: F) -> Option<R>
where
    F: FnOnce(&ObjectIdRegistry) -> R,
{
    let guard = REGISTRY.lock().ok()?;
    guard.as_ref().map(f)
}

/// Run a closure with exclusive write access to the global registry.
/// Initialises an empty registry on first call.
pub fn with_registry_mut<F, R>(f: F) -> Option<R>
where
    F: FnOnce(&mut ObjectIdRegistry) -> R,
{
    let mut guard = REGISTRY.lock().ok()?;
    if guard.is_none() {
        *guard = Some(ObjectIdRegistry::new());
    }
    guard.as_mut().map(f)
}

/// Reset the global registry (e.g. on scene change / disconnect).
pub fn reset_registry() {
    if let Ok(mut guard) = REGISTRY.lock() {
        *guard = None;
    }
}

// ── Registry types ─────────────────────────────────────────────────────────

/// One entry in the registry.
#[derive(Debug, Clone)]
pub struct RegistryEntry {
    /// Current Il2Cpp handle (may change when the game clones an object).
    pub handle: ObjectHandle,
    /// Wire‑level object type (0‑3 = server, 4 = switch, 7 = patch panel).
    pub object_type: u8,
}

/// Maps stable string IDs ↔ volatile Unity handles.
#[derive(Debug)]
pub struct ObjectIdRegistry {
    /// stable_id → entry
    by_id: HashMap<String, RegistryEntry>,
    /// handle → stable_id  (reverse index, kept in sync)
    by_handle: HashMap<ObjectHandle, String>,
    /// Monotonic counter used when generating IDs for objects that have none.
    next_generated: u32,
}

impl ObjectIdRegistry {
    pub fn new() -> Self {
        Self {
            by_id: HashMap::new(),
            by_handle: HashMap::new(),
            next_generated: 1,
        }
    }

    // ── Queries ────────────────────────────────────────────────────────

    /// Look up an entry by its stable ID.
    pub fn find_by_id(&self, stable_id: &str) -> Option<&RegistryEntry> {
        self.by_id.get(stable_id)
    }

    /// Look up the stable ID for a given handle.
    pub fn find_id_by_handle(&self, handle: ObjectHandle) -> Option<&str> {
        self.by_handle.get(&handle).map(|s| s.as_str())
    }

    /// Total number of tracked objects.
    pub fn len(&self) -> usize {
        self.by_id.len()
    }

    pub fn is_empty(&self) -> bool {
        self.by_id.is_empty()
    }

    /// Iterate over all `(stable_id, entry)` pairs.
    pub fn iter(&self) -> impl Iterator<Item = (&str, &RegistryEntry)> {
        self.by_id.iter().map(|(k, v)| (k.as_str(), v))
    }

    // ── Mutations ──────────────────────────────────────────────────────

    /// Register (or update) an object.
    ///
    /// If `stable_id` already exists the handle is updated.
    /// If the old handle pointed to a different ID, that reverse entry is
    /// cleaned up.
    pub fn register(
        &mut self,
        stable_id: impl Into<String>,
        handle: ObjectHandle,
        object_type: u8,
    ) {
        let stable_id = stable_id.into();

        // Remove stale reverse entry for the same handle (could point to a
        // different ID if the game reused an address).
        if let Some(old_id) = self.by_handle.remove(&handle) {
            if old_id != stable_id {
                self.by_id.remove(&old_id);
            }
        }

        // Remove stale reverse entry for the old handle of this ID.
        if let Some(old_entry) = self.by_id.get(&stable_id) {
            self.by_handle.remove(&old_entry.handle);
        }

        self.by_handle.insert(handle, stable_id.clone());
        self.by_id.insert(
            stable_id,
            RegistryEntry {
                handle,
                object_type,
            },
        );
    }

    /// Update only the handle for an existing stable ID (clone event).
    /// Returns `true` if the ID existed and was updated.
    pub fn update_handle(&mut self, stable_id: &str, new_handle: ObjectHandle) -> bool {
        if let Some(entry) = self.by_id.get_mut(stable_id) {
            // Remove old reverse
            self.by_handle.remove(&entry.handle);
            entry.handle = new_handle;
            self.by_handle.insert(new_handle, stable_id.to_string());
            true
        } else {
            false
        }
    }

    /// Remove an entry by stable ID.
    pub fn remove(&mut self, stable_id: &str) -> Option<RegistryEntry> {
        if let Some(entry) = self.by_id.remove(stable_id) {
            self.by_handle.remove(&entry.handle);
            Some(entry)
        } else {
            None
        }
    }

    /// Remove all entries.
    pub fn clear(&mut self) {
        self.by_id.clear();
        self.by_handle.clear();
    }

    /// Generate a new unique stable ID with the given prefix.
    /// E.g. `generate_id("PatchPanel")` → `"PatchPanel__g0001"`.
    pub fn generate_id(&mut self, prefix: &str) -> String {
        let id = format!("{}__g{:04}", prefix, self.next_generated);
        self.next_generated += 1;
        id
    }

    // ── Bulk population ────────────────────────────────────────────────

    /// Scan all game objects through the API and register them.
    ///
    /// * Objects that already have a non‑empty game ID keep it as their
    ///   stable ID.
    /// * Objects with an empty ID get a generated one, which is also
    ///   written back to the game via `obj_set_string_field`.
    pub fn populate_from_game(&mut self, api: &Api) {
        self.populate_type::<super::Server>(api, "Server");
        self.populate_type::<super::NetworkSwitch>(api, "Switch");
        self.populate_type::<super::PatchPanel>(api, "PatchPanel");

        crate::crash_log(&format!(
            "[Registry] Populated {} objects from game",
            self.len()
        ));
    }

    fn populate_type<T: WorldObject>(&mut self, api: &Api, prefix: &str) {
        let objects = T::find_all(api);
        for obj in &objects {
            let handle = obj.handle();
            let game_id = obj.id().to_string();

            let stable_id = if game_id.is_empty() {
                let generated = self.generate_id(prefix);
                // Write the generated ID back to the game object
                api.obj_set_string_field(handle, T::ID_FIELD, &generated);
                crate::crash_log(&format!(
                    "[Registry] Assigned '{}' to {} at handle {:?}",
                    generated, prefix, handle
                ));
                generated
            } else {
                game_id
            };

            self.register(stable_id, handle, T::OBJECT_TYPE.0);
        }
    }
}

impl Default for ObjectIdRegistry {
    fn default() -> Self {
        Self::new()
    }
}

// ── Tests ──────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    fn h(v: u64) -> ObjectHandle {
        ObjectHandle(v)
    }

    #[test]
    fn register_and_lookup() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("srv-1", h(100), 0);

        let entry = reg.find_by_id("srv-1").unwrap();
        assert_eq!(entry.handle, h(100));
        assert_eq!(entry.object_type, 0);

        assert_eq!(reg.find_id_by_handle(h(100)), Some("srv-1"));
    }

    #[test]
    fn update_handle_after_clone() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("srv-1", h(100), 0);

        assert!(reg.update_handle("srv-1", h(200)));

        assert_eq!(reg.find_by_id("srv-1").unwrap().handle, h(200));
        assert_eq!(reg.find_id_by_handle(h(200)), Some("srv-1"));
        assert_eq!(reg.find_id_by_handle(h(100)), None); // old handle gone
    }

    #[test]
    fn update_handle_nonexistent_returns_false() {
        let mut reg = ObjectIdRegistry::new();
        assert!(!reg.update_handle("nope", h(1)));
    }

    #[test]
    fn remove_clears_both_maps() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("sw-1", h(50), 4);

        let removed = reg.remove("sw-1").unwrap();
        assert_eq!(removed.handle, h(50));
        assert!(reg.find_by_id("sw-1").is_none());
        assert!(reg.find_id_by_handle(h(50)).is_none());
    }

    #[test]
    fn generate_id_increments() {
        let mut reg = ObjectIdRegistry::new();
        assert_eq!(reg.generate_id("PP"), "PP__g0001");
        assert_eq!(reg.generate_id("PP"), "PP__g0002");
        assert_eq!(reg.generate_id("Srv"), "Srv__g0003");
    }

    #[test]
    fn register_overwrites_stale_reverse() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("a", h(10), 0);
        reg.register("b", h(10), 4); // same handle, different ID

        // "b" now owns handle 10
        assert_eq!(reg.find_id_by_handle(h(10)), Some("b"));
        // "a" should have been cleaned up
        assert!(reg.find_by_id("a").is_none());
    }

    #[test]
    fn register_same_id_updates_handle() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("srv-1", h(10), 0);
        reg.register("srv-1", h(20), 0);

        assert_eq!(reg.find_by_id("srv-1").unwrap().handle, h(20));
        assert_eq!(reg.find_id_by_handle(h(20)), Some("srv-1"));
        assert_eq!(reg.find_id_by_handle(h(10)), None);
    }

    #[test]
    fn clear_empties_everything() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("a", h(1), 0);
        reg.register("b", h(2), 4);
        reg.clear();
        assert!(reg.is_empty());
        assert!(reg.find_by_id("a").is_none());
        assert!(reg.find_id_by_handle(h(1)).is_none());
    }

    #[test]
    fn iter_yields_all() {
        let mut reg = ObjectIdRegistry::new();
        reg.register("a", h(1), 0);
        reg.register("b", h(2), 4);
        let mut items: Vec<_> = reg.iter().map(|(id, _)| id.to_string()).collect();
        items.sort();
        assert_eq!(items, vec!["a", "b"]);
    }

    #[test]
    fn len_and_is_empty() {
        let mut reg = ObjectIdRegistry::new();
        assert!(reg.is_empty());
        assert_eq!(reg.len(), 0);
        reg.register("x", h(1), 0);
        assert!(!reg.is_empty());
        assert_eq!(reg.len(), 1);
    }

    #[test]
    fn with_registry_mut_auto_init() {
        // Reset to ensure clean state
        reset_registry();
        let len = with_registry_mut(|r| r.len());
        assert_eq!(len, Some(0));
    }

    #[test]
    fn with_registry_before_init_returns_none() {
        reset_registry();
        let result = with_registry(|r| r.len());
        assert_eq!(result, None);
    }
}
