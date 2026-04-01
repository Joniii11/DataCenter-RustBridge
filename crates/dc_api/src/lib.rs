//! FFI types and safe wrappers for writing Data Center mods in Rust.
//!
//! ```rust,ignore
//! use dc_api::*;
//!
//! static API: std::sync::OnceLock<Api> = std::sync::OnceLock::new();
//!
//! #[no_mangle]
//! pub extern "C" fn mod_info() -> ModInfo {
//!     ModInfo::new("my_mod", "My Mod", "1.0.0", "Author", "Description")
//! }
//!
//! #[no_mangle]
//! pub extern "C" fn mod_init(api: &'static GameAPI) -> bool {
//!     let api = unsafe { Api::from_raw(api) };
//!     let _ = API.set(api);
//!     true
//! }
//! ```

pub mod events;
pub use events::{Event, EventCategory, EventId};

use std::ffi::{c_char, CStr, CString};
use std::fmt;
use std::sync::OnceLock;

pub const API_VERSION: u32 = 2;

#[repr(C)]
pub struct ModInfo {
    pub id: *const c_char,
    pub name: *const c_char,
    pub version: *const c_char,
    pub author: *const c_char,
    pub description: *const c_char,
}

struct ModInfoStrings {
    _id: CString,
    _name: CString,
    _version: CString,
    _author: CString,
    _description: CString,
}

unsafe impl Send for ModInfoStrings {}
unsafe impl Sync for ModInfoStrings {}

static MOD_INFO_STRINGS: OnceLock<ModInfoStrings> = OnceLock::new();

impl ModInfo {
    pub fn new(id: &str, name: &str, version: &str, author: &str, description: &str) -> Self {
        let strings = MOD_INFO_STRINGS.get_or_init(|| ModInfoStrings {
            _id: CString::new(id).unwrap(),
            _name: CString::new(name).unwrap(),
            _version: CString::new(version).unwrap(),
            _author: CString::new(author).unwrap(),
            _description: CString::new(description).unwrap(),
        });

        ModInfo {
            id: strings._id.as_ptr(),
            name: strings._name.as_ptr(),
            version: strings._version.as_ptr(),
            author: strings._author.as_ptr(),
            description: strings._description.as_ptr(),
        }
    }
}

// function pointer table from C#, append-only
#[repr(C)]
pub struct GameAPI {
    pub api_version: u32,

    pub log_info: extern "C" fn(*const c_char),
    pub log_warning: extern "C" fn(*const c_char),
    pub log_error: extern "C" fn(*const c_char),

    pub get_player_money: extern "C" fn() -> f64,
    pub set_player_money: extern "C" fn(f64),

    pub get_time_scale: extern "C" fn() -> f32,
    pub set_time_scale: extern "C" fn(f32),

    pub get_server_count: extern "C" fn() -> u32,
    pub get_rack_count: extern "C" fn() -> u32,

    pub get_current_scene: extern "C" fn() -> *const c_char,

    pub get_player_xp: extern "C" fn() -> f64,
    pub set_player_xp: extern "C" fn(f64),

    pub get_player_reputation: extern "C" fn() -> f64,
    pub set_player_reputation: extern "C" fn(f64),

    pub get_time_of_day: extern "C" fn() -> f32,
    pub get_day: extern "C" fn() -> u32,
    pub get_seconds_in_full_day: extern "C" fn() -> f32,
    pub set_seconds_in_full_day: extern "C" fn(f32),

    pub get_switch_count: extern "C" fn() -> u32,

    pub get_satisfied_customer_count: extern "C" fn() -> u32,
}

unsafe impl Send for GameAPI {}
unsafe impl Sync for GameAPI {}

pub struct Api {
    raw: &'static GameAPI,
}

unsafe impl Send for Api {}
unsafe impl Sync for Api {}

impl Api {
    pub unsafe fn from_raw(raw: &'static GameAPI) -> Self {
        Self { raw }
    }

    pub fn version(&self) -> u32 {
        self.raw.api_version
    }

    pub fn log_info(&self, msg: &str) {
        if let Ok(c) = CString::new(msg) {
            (self.raw.log_info)(c.as_ptr());
        }
    }

    pub fn log_warning(&self, msg: &str) {
        if let Ok(c) = CString::new(msg) {
            (self.raw.log_warning)(c.as_ptr());
        }
    }

    pub fn log_error(&self, msg: &str) {
        if let Ok(c) = CString::new(msg) {
            (self.raw.log_error)(c.as_ptr());
        }
    }

    pub fn get_player_money(&self) -> f64 {
        (self.raw.get_player_money)()
    }

    pub fn set_player_money(&self, amount: f64) {
        (self.raw.set_player_money)(amount);
    }

    // 1.0 = normal, 0.0 = paused
    pub fn get_time_scale(&self) -> f32 {
        (self.raw.get_time_scale)()
    }

    pub fn set_time_scale(&self, scale: f32) {
        (self.raw.set_time_scale)(scale);
    }

    pub fn get_server_count(&self) -> u32 {
        (self.raw.get_server_count)()
    }

    pub fn get_rack_count(&self) -> u32 {
        (self.raw.get_rack_count)()
    }

    pub fn get_current_scene(&self) -> String {
        let ptr = (self.raw.get_current_scene)();
        if ptr.is_null() {
            return String::new();
        }
        unsafe { CStr::from_ptr(ptr) }
            .to_string_lossy()
            .into_owned()
    }

    // returns None if API version < 2
    pub fn get_player_xp(&self) -> Option<f64> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_player_xp)())
    }

    // returns false if API version < 2
    pub fn set_player_xp(&self, value: f64) -> bool {
        if self.raw.api_version < 2 {
            return false;
        }
        (self.raw.set_player_xp)(value);
        true
    }

    // returns None if API version < 2
    pub fn get_player_reputation(&self) -> Option<f64> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_player_reputation)())
    }

    // returns false if API version < 2
    pub fn set_player_reputation(&self, value: f64) -> bool {
        if self.raw.api_version < 2 {
            return false;
        }
        (self.raw.set_player_reputation)(value);
        true
    }

    // 0.0 = midnight, 0.5 = noon, 1.0 = end of day
    // returns None if API version < 2
    pub fn get_time_of_day(&self) -> Option<f32> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_time_of_day)())
    }

    // returns None if API version < 2
    pub fn get_day(&self) -> Option<u32> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_day)())
    }

    // returns None if API version < 2
    pub fn get_seconds_in_full_day(&self) -> Option<f32> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_seconds_in_full_day)())
    }

    // lower values = faster days, returns false if API version < 2
    pub fn set_seconds_in_full_day(&self, seconds: f32) -> bool {
        if self.raw.api_version < 2 {
            return false;
        }
        (self.raw.set_seconds_in_full_day)(seconds);
        true
    }

    // returns None if API version < 2
    pub fn get_switch_count(&self) -> Option<u32> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_switch_count)())
    }

    // returns None if API version < 2
    pub fn get_satisfied_customer_count(&self) -> Option<u32> {
        if self.raw.api_version < 2 {
            return None;
        }
        Some((self.raw.get_satisfied_customer_count)())
    }
}

impl fmt::Debug for Api {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Api")
            .field("api_version", &self.raw.api_version)
            .finish()
    }
}

pub type ModInfoFn = unsafe extern "C" fn() -> ModInfo;
pub type ModInitFn = unsafe extern "C" fn(api: &'static GameAPI) -> bool;
pub type ModUpdateFn = unsafe extern "C" fn(delta_time: f32);
pub type ModFixedUpdateFn = unsafe extern "C" fn(delta_time: f32);
pub type ModOnSceneLoadedFn = unsafe extern "C" fn(scene_name: *const c_char);
pub type ModShutdownFn = unsafe extern "C" fn();
pub type ModOnEventFn = events::ModOnEventFn;
