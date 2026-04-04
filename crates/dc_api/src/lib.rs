//! FFI types and safe wrappers for writing Data Center mods in Rust.
//!
//! # With macros (recommended)
//!
//! ```rust,ignore
//! use dc_api::*;
//!
//! #[dc_api::mod_entry(
//!     id = "my_mod",
//!     name = "My Mod",
//!     version = "1.0.0",
//!     author = "Author",
//!     description = "A cool mod",
//! )]
//! fn init(api: &Api) -> bool {
//!     api.log_info("Hello!");
//!     true
//! }
//!
//! #[dc_api::on_update]
//! fn update(api: &Api, dt: f32) {
//!     // called every frame
//! }
//! ```
//!
//! # Without macros (manual)
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

// Re-export proc macros so users can write `#[dc_api::mod_entry(...)]` etc.
pub use dc_api_macros::{mod_entry, on_event, on_scene_loaded, on_shutdown, on_update};

use std::ffi::{c_char, CStr, CString};
use std::fmt;
use std::io::Write;
use std::path::PathBuf;
use std::sync::OnceLock;

pub const API_VERSION: u32 = 9;

static __MOD_API: OnceLock<Api> = OnceLock::new();
static __CRASH_LOG_NAME: OnceLock<String> = OnceLock::new();

#[doc(hidden)]
pub fn __internal_set_mod_api(api: Api) {
    let _ = __MOD_API.set(api);
}

/// Retrieve the stored API reference.
#[doc(hidden)]
pub fn __internal_mod_api() -> Option<&'static Api> {
    __MOD_API.get()
}

/// Also exposed as a nice public function for manual use.
pub fn mod_api() -> Option<&'static Api> {
    __MOD_API.get()
}

/// Set the crash log filename (called by generated `mod_init`).
#[doc(hidden)]
pub fn __internal_set_crash_log(name: &str) {
    let _ = __CRASH_LOG_NAME.set(name.to_owned());
}

/// Write a message to the mod's crash log file.
#[doc(hidden)]
pub fn __internal_crash_log(msg: &str) {
    let name = __CRASH_LOG_NAME
        .get()
        .map(|s| s.as_str())
        .unwrap_or("dc_mod_crash.log");
    let path = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.join(name)))
        .unwrap_or_else(|| PathBuf::from(name));
    let _ = (|| -> std::io::Result<()> {
        let mut f = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&path)?;
        let ts = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        writeln!(f, "[{}] {}", ts, msg)?;
        Ok(())
    })();
}

/// Also exposed as a nice public function for manual use.
pub fn crash_log(msg: &str) {
    __internal_crash_log(msg);
}

/// Convert a panic payload to a printable string.
#[doc(hidden)]
pub fn __internal_panic_to_string(payload: &Box<dyn std::any::Any + Send>) -> String {
    if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_owned()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "<non-string panic>".to_owned()
    }
}

/// Install a panic hook that writes to the crash log (called by generated `mod_init`).
#[doc(hidden)]
pub fn __internal_setup_panic_hook() {
    std::panic::set_hook(Box::new(|info| {
        let mut message = String::from("PANIC: ");

        if let Some(s) = info.payload().downcast_ref::<&str>() {
            message.push_str(s);
        } else if let Some(s) = info.payload().downcast_ref::<String>() {
            message.push_str(s);
        } else {
            message.push_str("<non-string panic payload>");
        }

        if let Some(loc) = info.location() {
            message.push_str(&format!(
                " at {}:{}:{}",
                loc.file(),
                loc.line(),
                loc.column()
            ));
        }

        let bt = std::backtrace::Backtrace::capture();
        if bt.status() == std::backtrace::BacktraceStatus::Captured {
            message.push_str(&format!("\nBacktrace:\n{}", bt));
        }

        __internal_crash_log(&message);
    }));
}

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

    pub set_netwatch_enabled: extern "C" fn(u32), // 1 = enable, 0 = disable
    pub is_netwatch_enabled: extern "C" fn() -> u32, // 1 = enabled, 0 = disabled
    pub get_netwatch_stats: extern "C" fn() -> u32, // total dispatches count

    pub get_broken_server_count: extern "C" fn() -> u32,
    pub get_broken_switch_count: extern "C" fn() -> u32,
    pub get_eol_server_count: extern "C" fn() -> u32,
    pub get_eol_switch_count: extern "C" fn() -> u32,
    pub get_free_technician_count: extern "C" fn() -> u32,
    pub get_total_technician_count: extern "C" fn() -> u32,
    pub dispatch_repair_server: extern "C" fn() -> i32,
    pub dispatch_repair_switch: extern "C" fn() -> i32,
    pub dispatch_replace_server: extern "C" fn() -> i32,
    pub dispatch_replace_switch: extern "C" fn() -> i32,

    pub register_custom_employee:
        extern "C" fn(*const c_char, *const c_char, *const c_char, f32, f32, u32) -> i32,
    pub is_custom_employee_hired: extern "C" fn(*const c_char) -> u32,
    pub fire_custom_employee: extern "C" fn(*const c_char) -> i32,
    pub register_salary: extern "C" fn(i32) -> i32,

    pub show_notification: extern "C" fn(*const c_char) -> i32,
    pub get_money_per_second: extern "C" fn() -> f32,
    pub get_expenses_per_second: extern "C" fn() -> f32,
    pub get_xp_per_second: extern "C" fn() -> f32,
    pub is_game_paused: extern "C" fn() -> u32,
    pub set_game_paused: extern "C" fn(u32),
    pub get_difficulty: extern "C" fn() -> i32,
    pub trigger_save: extern "C" fn() -> i32,

    pub steam_get_my_id: extern "C" fn() -> u64,
    pub steam_get_friend_name: extern "C" fn(steam_id: u64) -> *const c_char,
    pub steam_create_lobby: extern "C" fn(lobby_type: u32, max_players: u32) -> i32,
    pub steam_join_lobby: extern "C" fn(lobby_id: u64) -> i32,
    pub steam_leave_lobby: extern "C" fn(),
    pub steam_get_lobby_id: extern "C" fn() -> u64,
    pub steam_get_lobby_owner: extern "C" fn() -> u64,
    pub steam_get_lobby_member_count: extern "C" fn() -> u32,
    pub steam_get_lobby_member_by_index: extern "C" fn(index: u32) -> u64,
    pub steam_set_lobby_data: extern "C" fn(key: *const c_char, value: *const c_char) -> i32,
    pub steam_get_lobby_data: extern "C" fn(key: *const c_char) -> *const c_char,
    pub steam_send_p2p: extern "C" fn(target: u64, data: *const u8, len: u32, reliable: u32) -> i32,
    pub steam_is_p2p_available: extern "C" fn(out_size: *mut u32) -> u32,
    pub steam_read_p2p: extern "C" fn(buf: *mut u8, buf_len: u32, out_sender: *mut u64) -> u32,
    pub steam_accept_p2p: extern "C" fn(remote: u64),
    pub steam_poll_event: extern "C" fn(out_type: *mut u32, out_data: *mut u64) -> u32,
    pub get_player_position:
        extern "C" fn(out_x: *mut f32, out_y: *mut f32, out_z: *mut f32, out_ry: *mut f32),

    pub config_register_bool: extern "C" fn(
        mod_id: *const c_char,
        key: *const c_char,
        display_name: *const c_char,
        default_value: u32,
        description: *const c_char,
    ) -> u32,
    pub config_register_int: extern "C" fn(
        mod_id: *const c_char,
        key: *const c_char,
        display_name: *const c_char,
        default_value: i32,
        min: i32,
        max: i32,
        description: *const c_char,
    ) -> u32,
    pub config_register_float: extern "C" fn(
        mod_id: *const c_char,
        key: *const c_char,
        display_name: *const c_char,
        default_value: f32,
        min: f32,
        max: f32,
        description: *const c_char,
    ) -> u32,
    pub config_get_bool: extern "C" fn(mod_id: *const c_char, key: *const c_char) -> u32,
    pub config_get_int: extern "C" fn(mod_id: *const c_char, key: *const c_char) -> i32,
    pub config_get_float: extern "C" fn(mod_id: *const c_char, key: *const c_char) -> f32,

    pub spawn_character: extern "C" fn(
        prefab_idx: u32,
        x: f32,
        y: f32,
        z: f32,
        rot_y: f32,
        name: *const c_char,
    ) -> u32,
    pub destroy_entity: extern "C" fn(entity_id: u32),
    pub set_entity_position: extern "C" fn(entity_id: u32, x: f32, y: f32, z: f32, rot_y: f32),
    pub is_entity_ready: extern "C" fn(entity_id: u32) -> u32,
    pub set_entity_animation: extern "C" fn(entity_id: u32, speed: f32, is_walking: u32),
    pub get_prefab_count: extern "C" fn() -> u32,
    pub set_entity_name: extern "C" fn(entity_id: u32, name: *const c_char),
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

    /// Enable or disable the NetWatch auto-repair system.
    pub fn set_netwatch_enabled(&self, enabled: bool) -> bool {
        if self.raw.api_version < 3 {
            return false;
        }
        (self.raw.set_netwatch_enabled)(if enabled { 1 } else { 0 });
        true
    }

    /// Check if NetWatch is currently enabled.
    pub fn is_netwatch_enabled(&self) -> Option<bool> {
        if self.raw.api_version < 3 {
            return None;
        }
        Some((self.raw.is_netwatch_enabled)() != 0)
    }

    /// Get total number of technician dispatches by NetWatch.
    pub fn get_netwatch_stats(&self) -> Option<u32> {
        if self.raw.api_version < 3 {
            return None;
        }
        Some((self.raw.get_netwatch_stats)())
    }

    /// Number of currently broken servers.
    pub fn get_broken_server_count(&self) -> Option<u32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.get_broken_server_count)())
    }

    /// Number of currently broken switches.
    pub fn get_broken_switch_count(&self) -> Option<u32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.get_broken_switch_count)())
    }

    /// Number of servers at/past end-of-life (eolTime <= 0, not broken).
    pub fn get_eol_server_count(&self) -> Option<u32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.get_eol_server_count)())
    }

    /// Number of switches with EOL warnings (existingWarningSigns > 0, not broken).
    pub fn get_eol_switch_count(&self) -> Option<u32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.get_eol_switch_count)())
    }

    /// Number of technicians currently not busy.
    pub fn get_free_technician_count(&self) -> Option<u32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.get_free_technician_count)())
    }

    /// Total number of technicians (busy + free).
    pub fn get_total_technician_count(&self) -> Option<u32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.get_total_technician_count)())
    }

    /// Dispatch a technician to repair the first unassigned broken server.
    /// Returns: 1 = dispatched, 0 = no target, -1 = no free technician.
    pub fn dispatch_repair_server(&self) -> Option<i32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.dispatch_repair_server)())
    }

    /// Dispatch a technician to repair the first unassigned broken switch.
    /// Returns: 1 = dispatched, 0 = no target, -1 = no free technician.
    pub fn dispatch_repair_switch(&self) -> Option<i32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.dispatch_repair_switch)())
    }

    /// Dispatch a technician to replace the first unassigned EOL server.
    /// Returns: 1 = dispatched, 0 = no target, -1 = no free technician.
    pub fn dispatch_replace_server(&self) -> Option<i32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.dispatch_replace_server)())
    }

    /// Dispatch a technician to replace the first unassigned EOL switch.
    /// Returns: 1 = dispatched, 0 = no target, -1 = no free technician.
    pub fn dispatch_replace_switch(&self) -> Option<i32> {
        if self.raw.api_version < 4 {
            return None;
        }
        Some((self.raw.dispatch_replace_switch)())
    }

    /// Register a custom employee that appears in the HR System.
    /// - `id`: unique identifier (e.g. "sysadmin")
    /// - `name`: display name (e.g. "SysAdmin")
    /// - `description`: tooltip text
    /// - `salary_per_hour`: displayed salary
    /// - `required_reputation`: reputation needed to hire
    /// - `confirm_dialogs`: show confirmation dialogs when hiring
    ///
    /// Returns: 1 = success, 0 = duplicate/error
    pub fn register_custom_employee(
        &self,
        id: &str,
        name: &str,
        description: &str,
        salary_per_hour: f32,
        required_reputation: f32,
        confirm_dialogs: bool,
    ) -> Option<i32> {
        if self.raw.api_version < 5 {
            return None;
        }
        let c_id = CString::new(id).ok()?;
        let c_name = CString::new(name).ok()?;
        let c_desc = CString::new(description).ok()?;
        Some((self.raw.register_custom_employee)(
            c_id.as_ptr(),
            c_name.as_ptr(),
            c_desc.as_ptr(),
            salary_per_hour,
            required_reputation,
            confirm_dialogs as u32,
        ))
    }

    /// Check if a custom employee is currently hired.
    pub fn is_custom_employee_hired(&self, id: &str) -> Option<bool> {
        if self.raw.api_version < 5 {
            return None;
        }
        let c_id = match CString::new(id) {
            Ok(c) => c,
            Err(_) => return None,
        };
        Some((self.raw.is_custom_employee_hired)(c_id.as_ptr()) != 0)
    }

    /// Programmatically fire a custom employee.
    /// Returns: 1 = fired, 0 = not found/not hired
    pub fn fire_custom_employee(&self, id: &str) -> Option<i32> {
        if self.raw.api_version < 5 {
            return None;
        }
        let c_id = match CString::new(id) {
            Ok(c) => c,
            Err(_) => return None,
        };
        Some((self.raw.fire_custom_employee)(c_id.as_ptr()))
    }

    /// Register a recurring monthly salary expense in the game's BalanceSheet.
    /// Pass a negative value to remove an expense (e.g. when firing).
    /// Returns 1 on success, 0 on error.
    pub fn register_salary(&self, monthly_salary: i32) -> Option<i32> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.register_salary)(monthly_salary))
    }

    pub fn show_notification(&self, message: &str) -> Option<i32> {
        if self.raw.api_version < 5 {
            return None;
        }
        let c_msg = CString::new(message).ok()?;
        Some((self.raw.show_notification)(c_msg.as_ptr()))
    }

    pub fn get_money_per_second(&self) -> Option<f32> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.get_money_per_second)())
    }

    pub fn get_expenses_per_second(&self) -> Option<f32> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.get_expenses_per_second)())
    }

    pub fn get_xp_per_second(&self) -> Option<f32> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.get_xp_per_second)())
    }

    pub fn is_game_paused(&self) -> Option<bool> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.is_game_paused)() != 0)
    }

    pub fn set_game_paused(&self, paused: bool) {
        if self.raw.api_version >= 5 {
            (self.raw.set_game_paused)(paused as u32);
        }
    }

    pub fn get_difficulty(&self) -> Option<i32> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.get_difficulty)())
    }

    pub fn trigger_save(&self) -> Option<i32> {
        if self.raw.api_version < 5 {
            return None;
        }
        Some((self.raw.trigger_save)())
    }

    // v7 — Steam / Multiplayer

    pub fn steam_get_my_id(&self) -> Option<u64> {
        if self.raw.api_version < 7 {
            return None;
        }
        let id = (self.raw.steam_get_my_id)();
        if id == 0 {
            None
        } else {
            Some(id)
        }
    }

    pub fn steam_get_friend_name(&self, steam_id: u64) -> Option<String> {
        if self.raw.api_version < 7 {
            return None;
        }
        let ptr = (self.raw.steam_get_friend_name)(steam_id);
        if ptr.is_null() {
            return None;
        }
        unsafe { CStr::from_ptr(ptr).to_str().ok().map(|s| s.to_string()) }
    }

    pub fn steam_create_lobby(&self, lobby_type: u32, max_players: u32) -> Option<i32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_create_lobby)(lobby_type, max_players))
    }

    pub fn steam_join_lobby(&self, lobby_id: u64) -> Option<i32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_join_lobby)(lobby_id))
    }

    pub fn steam_leave_lobby(&self) {
        if self.raw.api_version < 7 {
            return;
        }
        (self.raw.steam_leave_lobby)()
    }

    pub fn steam_get_lobby_id(&self) -> Option<u64> {
        if self.raw.api_version < 7 {
            return None;
        }
        let id = (self.raw.steam_get_lobby_id)();
        if id == 0 {
            None
        } else {
            Some(id)
        }
    }

    pub fn steam_get_lobby_owner(&self) -> Option<u64> {
        if self.raw.api_version < 7 {
            return None;
        }
        let id = (self.raw.steam_get_lobby_owner)();
        if id == 0 {
            None
        } else {
            Some(id)
        }
    }

    pub fn steam_get_lobby_member_count(&self) -> Option<u32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_get_lobby_member_count)())
    }

    pub fn steam_get_lobby_member_by_index(&self, index: u32) -> Option<u64> {
        if self.raw.api_version < 7 {
            return None;
        }
        let id = (self.raw.steam_get_lobby_member_by_index)(index);
        if id == 0 {
            None
        } else {
            Some(id)
        }
    }

    pub fn steam_set_lobby_data(&self, key: &str, value: &str) -> Option<i32> {
        if self.raw.api_version < 7 {
            return None;
        }
        let key_c = CString::new(key).ok()?;
        let val_c = CString::new(value).ok()?;
        Some((self.raw.steam_set_lobby_data)(
            key_c.as_ptr(),
            val_c.as_ptr(),
        ))
    }

    pub fn steam_get_lobby_data(&self, key: &str) -> Option<String> {
        if self.raw.api_version < 7 {
            return None;
        }
        let key_c = CString::new(key).ok()?;
        let ptr = (self.raw.steam_get_lobby_data)(key_c.as_ptr());
        if ptr.is_null() {
            return None;
        }
        unsafe { CStr::from_ptr(ptr).to_str().ok().map(|s| s.to_string()) }
    }

    pub fn steam_send_p2p(&self, target: u64, data: &[u8], reliable: u32) -> Option<i32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_send_p2p)(
            target,
            data.as_ptr(),
            data.len() as u32,
            reliable,
        ))
    }

    pub fn steam_is_p2p_available(&self, out_size: &mut u32) -> Option<u32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_is_p2p_available)(out_size as *mut u32))
    }

    pub fn steam_read_p2p(&self, buf: &mut [u8], out_sender: &mut u64) -> Option<u32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_read_p2p)(
            buf.as_mut_ptr(),
            buf.len() as u32,
            out_sender as *mut u64,
        ))
    }

    pub fn steam_accept_p2p(&self, remote: u64) {
        if self.raw.api_version < 7 {
            return;
        }
        (self.raw.steam_accept_p2p)(remote)
    }

    pub fn steam_poll_event(&self, out_type: &mut u32, out_data: &mut u64) -> Option<u32> {
        if self.raw.api_version < 7 {
            return None;
        }
        Some((self.raw.steam_poll_event)(
            out_type as *mut u32,
            out_data as *mut u64,
        ))
    }

    pub fn get_player_position(&self) -> Option<(f32, f32, f32, f32)> {
        if self.raw.api_version < 7 {
            return None;
        }
        let (mut x, mut y, mut z, mut ry) = (0f32, 0f32, 0f32, 0f32);
        (self.raw.get_player_position)(&mut x, &mut y, &mut z, &mut ry);
        Some((x, y, z, ry))
    }

    // ── v8 — Mod Configuration ──────────────────────────────────────────

    /// Register a boolean config entry for this mod.
    /// Returns Some(1) on success, Some(0) if key already exists.
    /// Returns None if API version < 8.
    pub fn config_register_bool(
        &self,
        mod_id: &str,
        key: &str,
        display_name: &str,
        default_value: bool,
        description: &str,
    ) -> Option<u32> {
        if self.version() < 8 {
            return None;
        }
        let c_mod_id = CString::new(mod_id).ok()?;
        let c_key = CString::new(key).ok()?;
        let c_display_name = CString::new(display_name).ok()?;
        let c_description = CString::new(description).ok()?;
        Some((self.raw.config_register_bool)(
            c_mod_id.as_ptr(),
            c_key.as_ptr(),
            c_display_name.as_ptr(),
            if default_value { 1 } else { 0 },
            c_description.as_ptr(),
        ))
    }

    /// Register an integer config entry for this mod.
    /// Returns Some(1) on success, Some(0) if key already exists.
    pub fn config_register_int(
        &self,
        mod_id: &str,
        key: &str,
        display_name: &str,
        default_value: i32,
        min: i32,
        max: i32,
        description: &str,
    ) -> Option<u32> {
        if self.version() < 8 {
            return None;
        }
        let c_mod_id = CString::new(mod_id).ok()?;
        let c_key = CString::new(key).ok()?;
        let c_display_name = CString::new(display_name).ok()?;
        let c_description = CString::new(description).ok()?;
        Some((self.raw.config_register_int)(
            c_mod_id.as_ptr(),
            c_key.as_ptr(),
            c_display_name.as_ptr(),
            default_value,
            min,
            max,
            c_description.as_ptr(),
        ))
    }

    /// Register a float config entry for this mod.
    /// Returns Some(1) on success, Some(0) if key already exists.
    pub fn config_register_float(
        &self,
        mod_id: &str,
        key: &str,
        display_name: &str,
        default_value: f32,
        min: f32,
        max: f32,
        description: &str,
    ) -> Option<u32> {
        if self.version() < 8 {
            return None;
        }
        let c_mod_id = CString::new(mod_id).ok()?;
        let c_key = CString::new(key).ok()?;
        let c_display_name = CString::new(display_name).ok()?;
        let c_description = CString::new(description).ok()?;
        Some((self.raw.config_register_float)(
            c_mod_id.as_ptr(),
            c_key.as_ptr(),
            c_display_name.as_ptr(),
            default_value,
            min,
            max,
            c_description.as_ptr(),
        ))
    }

    /// Get a boolean config value. Returns Some(true/false) or None if not found or API < 8.
    pub fn config_get_bool(&self, mod_id: &str, key: &str) -> Option<bool> {
        if self.version() < 8 {
            return None;
        }
        let c_mod_id = CString::new(mod_id).ok()?;
        let c_key = CString::new(key).ok()?;
        let result = (self.raw.config_get_bool)(c_mod_id.as_ptr(), c_key.as_ptr());
        if result == 0xFFFFFFFF {
            None
        } else {
            Some(result != 0)
        }
    }

    /// Get an integer config value. Returns Some(value) or None if API < 8.
    pub fn config_get_int(&self, mod_id: &str, key: &str) -> Option<i32> {
        if self.version() < 8 {
            return None;
        }
        let c_mod_id = CString::new(mod_id).ok()?;
        let c_key = CString::new(key).ok()?;
        Some((self.raw.config_get_int)(c_mod_id.as_ptr(), c_key.as_ptr()))
    }

    /// Get a float config value. Returns Some(value) or None if API < 8.
    pub fn config_get_float(&self, mod_id: &str, key: &str) -> Option<f32> {
        if self.version() < 8 {
            return None;
        }
        let c_mod_id = CString::new(mod_id).ok()?;
        let c_key = CString::new(key).ok()?;
        Some((self.raw.config_get_float)(
            c_mod_id.as_ptr(),
            c_key.as_ptr(),
        ))
    }

    /// Spawn a UMA character at the given world position
    pub fn spawn_character(
        &self,
        prefab_idx: u32,
        x: f32,
        y: f32,
        z: f32,
        rot_y: f32,
        name: &str,
    ) -> Option<u32> {
        if self.version() < 9 {
            return None;
        }
        let c_name = CString::new(name).ok()?;
        let id = (self.raw.spawn_character)(prefab_idx, x, y, z, rot_y, c_name.as_ptr());
        if id == 0 {
            None
        } else {
            Some(id)
        }
    }

    /// Destroy entity by id
    pub fn destroy_entity(&self, entity_id: u32) {
        if self.version() >= 9 {
            (self.raw.destroy_entity)(entity_id);
        }
    }

    //FIXME correct rotation it's kind of broken
    /// Update entity pos and rot
    pub fn set_entity_position(&self, entity_id: u32, x: f32, y: f32, z: f32, rot_y: f32) {
        if self.version() >= 9 {
            (self.raw.set_entity_position)(entity_id, x, y, z, rot_y);
        }
    }

    /// Check if UMA mesh has finished generating
    pub fn is_entity_ready(&self, entity_id: u32) -> Option<bool> {
        if self.version() < 9 {
            return None;
        }
        Some((self.raw.is_entity_ready)(entity_id) != 0)
    }

    /// Drive the entitys animation with a speed value and walking flag
    pub fn set_entity_animation(&self, entity_id: u32, speed: f32, is_walking: bool) {
        if self.version() >= 9 {
            (self.raw.set_entity_animation)(entity_id, speed, if is_walking { 1 } else { 0 });
        }
    }

    /// Get the number of available character prefabs
    pub fn get_prefab_count(&self) -> Option<u32> {
        if self.version() < 9 {
            return None;
        }
        Some((self.raw.get_prefab_count)())
    }

    /// Update the nametag text for an entity
    pub fn set_entity_name(&self, entity_id: u32, name: &str) {
        if self.version() >= 9 {
            if let Ok(c) = CString::new(name) {
                (self.raw.set_entity_name)(entity_id, c.as_ptr());
            }
        }
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
