//! Infinite Money mod — example for the Data Center modloader.

use dc_api::events::{self, Event};
use dc_api::*;
use std::ffi::c_char;
use std::io::Write;
use std::path::PathBuf;
use std::sync::OnceLock;

static API: OnceLock<Api> = OnceLock::new();

// crash log helpers

// path to crash log next to the exe, falls back to cwd
fn crash_log_path() -> PathBuf {
    let base = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .unwrap_or_else(|| PathBuf::from("."));
    base.join("dc_modloader_crash.log")
}

// best-effort append to crash log, never panics
fn crash_log(msg: &str) {
    let _ = (|| -> std::io::Result<()> {
        let mut f = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(crash_log_path())?;
        // no chrono dep, epoch seconds is fine for crash forensics
        let timestamp = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        writeln!(f, "[{}] {}", timestamp, msg)?;
        Ok(())
    })();
}

// install panic hook that dumps to crash log — call early in mod_init
fn setup_panic_hook() {
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
        let bt_status = bt.status();
        if bt_status == std::backtrace::BacktraceStatus::Captured {
            message.push_str(&format!("\nBacktrace:\n{}", bt));
        }

        crash_log(&message);
    }));
}

fn api() -> Option<&'static Api> {
    API.get()
}

fn panic_payload_to_string(payload: &Box<dyn std::any::Any + Send>) -> String {
    if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_owned()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "<non-string panic>".to_owned()
    }
}

// exported FFI functions, each wrapped in catch_unwind

#[no_mangle]
pub extern "C" fn mod_info() -> ModInfo {
    let result = std::panic::catch_unwind(|| {
        ModInfo::new(
            "infinite_money",
            "Infinite Money",
            "1.0.0",
            "Joniii",
            "Gives you $999,999 and logs game events for now ig",
        )
    });

    match result {
        Ok(info) => info,
        Err(e) => {
            crash_log(&format!(
                "[mod_info] caught panic: {}",
                panic_payload_to_string(&e)
            ));
            // null pointers — loader treats this as invalid mod
            ModInfo {
                id: std::ptr::null(),
                name: std::ptr::null(),
                version: std::ptr::null(),
                author: std::ptr::null(),
                description: std::ptr::null(),
            }
        }
    }
}

#[no_mangle]
pub extern "C" fn mod_init(game_api: &'static GameAPI) -> bool {
    // panic hook first, before anything that could blow up
    setup_panic_hook();

    crash_log("[mod_init] >>> enter");

    let result = std::panic::catch_unwind(|| {
        let api = unsafe { Api::from_raw(game_api) };

        api.log_info("[InfiniteMoney] Mod loaded!");
        api.log_info(&format!("[InfiniteMoney] API version: {}", api.version()));

        let money = api.get_player_money();
        api.log_info(&format!("[InfiniteMoney] Current money: ${:.2}", money));

        if api.version() >= 2 {
            if let Some(xp) = api.get_player_xp() {
                api.log_info(&format!("[InfiniteMoney] Player XP: {:.1}", xp));
            }
            if let Some(rep) = api.get_player_reputation() {
                api.log_info(&format!("[InfiniteMoney] Reputation: {:.1}", rep));
            }
            if let Some(day) = api.get_day() {
                let tod = api.get_time_of_day().unwrap_or(0.0);
                let hours = (tod * 24.0) as u32;
                let minutes = ((tod * 24.0 - hours as f32) * 60.0) as u32;
                api.log_info(&format!(
                    "[InfiniteMoney] Day {}, Time {:02}:{:02}",
                    day, hours, minutes
                ));
            }
            if let Some(secs) = api.get_seconds_in_full_day() {
                api.log_info(&format!(
                    "[InfiniteMoney] Day length: {:.0}s real-time",
                    secs
                ));
            }
        }

        api.log_info("[InfiniteMoney] Listening for game events.");

        let _ = API.set(api);
        true
    });

    match result {
        Ok(v) => {
            crash_log("[mod_init] <<< exit (success)");
            v
        }
        Err(e) => {
            crash_log(&format!(
                "[mod_init] caught panic: {}",
                panic_payload_to_string(&e)
            ));
            crash_log("[mod_init] <<< exit (panic, returning false)");
            false
        }
    }
}

#[no_mangle]
pub extern "C" fn mod_update(_delta_time: f32) {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Some(api) = api() else {
            crash_log("[mod_update] API not initialised, skipping");
            return;
        };
        let money = api.get_player_money();
        if money < 999_999.0 {
            api.set_player_money(999_999.0);
        }
        let rep = api.get_player_reputation();
        if let Some(rep) = rep {
            if rep < 99_999.0 {
                api.set_player_reputation(99_999.0);
            }
        }
        let xp = api.get_player_xp();
        if let Some(xp) = xp {
            if xp < 999_999.0 {
                api.set_player_xp(999_999.0);
            }
        }
    }));

    if let Err(e) = result {
        crash_log(&format!(
            "[mod_update] caught panic: {}",
            panic_payload_to_string(&e)
        ));
    }
}

#[no_mangle]
pub extern "C" fn mod_on_scene_loaded(scene_name: *const c_char) {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if scene_name.is_null() {
            return;
        }
        let name = unsafe { std::ffi::CStr::from_ptr(scene_name) }.to_string_lossy();
        let Some(api) = api() else {
            crash_log("[mod_on_scene_loaded] API not initialised, skipping");
            return;
        };

        api.log_info(&format!("[InfiniteMoney] Scene loaded: {}", name));

        let servers = api.get_server_count();
        let racks = api.get_rack_count();
        api.log_info(&format!(
            "[InfiniteMoney] Data center: {} servers, {} racks",
            servers, racks
        ));

        if let Some(switches) = api.get_switch_count() {
            api.log_info(&format!("[InfiniteMoney] Network switches: {}", switches));
        }
        if let Some(customers) = api.get_satisfied_customer_count() {
            api.log_info(&format!(
                "[InfiniteMoney] Satisfied customers: {}",
                customers
            ));
        }
    }));

    if let Err(e) = result {
        crash_log(&format!(
            "[mod_on_scene_loaded] caught panic: {}",
            panic_payload_to_string(&e)
        ));
    }
}

#[no_mangle]
pub extern "C" fn mod_on_event(event_id: u32, event_data: *const u8, data_size: u32) {
    crash_log(&format!("[mod_on_event] >>> enter (event_id={})", event_id));

    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Some(event) = events::decode(event_id, event_data, data_size) else {
            return;
        };

        let Some(api) = api() else {
            crash_log("[mod_on_event] API not initialised, skipping");
            return;
        };

        match event {
            Event::MoneyChanged {
                old_value,
                new_value,
                delta,
            } => {
                if delta.abs() > 0.01 && (old_value - 999_999.0).abs() > 1.0 {
                    api.log_info(&format!(
                        "[InfiniteMoney] Money changed: ${:.2} -> ${:.2} (delta: {:+.2})",
                        old_value, new_value, delta
                    ));
                }
            }
            Event::XpChanged {
                old_value,
                new_value,
                delta,
            } => {
                api.log_info(&format!(
                    "[InfiniteMoney] XP gained! {:.1} -> {:.1} (+{:.1})",
                    old_value, new_value, delta
                ));
            }
            Event::ReputationChanged {
                old_value,
                new_value,
                delta,
            } => {
                let dir = if delta > 0.0 { "up" } else { "down" };
                api.log_info(&format!(
                    "[InfiniteMoney] Reputation {}: {:.1} -> {:.1} ({:+.1})",
                    dir, old_value, new_value, delta
                ));
            }
            Event::ServerPowered { powered_on } => {
                let state = if powered_on { "ON" } else { "OFF" };
                api.log_info(&format!("[InfiniteMoney] Server powered {}", state));
            }
            Event::ServerBroken => {
                api.log_warning("[InfiniteMoney] A server broke down!");
            }
            Event::ServerRepaired => {
                api.log_info("[InfiniteMoney] Server repaired.");
            }
            Event::ServerInstalled => {
                api.log_info("[InfiniteMoney] Server installed in rack.");
            }
            Event::CableConnected => {
                api.log_info("[InfiniteMoney] Cable connected to server.");
            }
            Event::CableDisconnected => {
                api.log_info("[InfiniteMoney] Cable disconnected from server.");
            }
            Event::ServerCustomerChanged { new_customer_id } => {
                api.log_info(&format!(
                    "[InfiniteMoney] Server customer changed to ID {}",
                    new_customer_id
                ));
            }
            Event::ServerAppChanged { new_app_id } => {
                api.log_info(&format!(
                    "[InfiniteMoney] Server app changed to ID {}",
                    new_app_id
                ));
            }
            Event::DayEnded { day } => {
                api.log_info(&format!(
                    "[InfiniteMoney] Day {} started! Money: ${:.2}",
                    day,
                    api.get_player_money()
                ));
            }
            Event::CustomerAccepted { customer_id } => {
                api.log_info(&format!(
                    "[InfiniteMoney] New customer accepted (ID: {})",
                    customer_id
                ));
            }
            Event::CustomerSatisfied { customer_base_id } => {
                api.log_info(&format!(
                    "[InfiniteMoney] Customer base {} is now fully satisfied!",
                    customer_base_id
                ));
            }
            Event::CustomerUnsatisfied { customer_base_id } => {
                api.log_warning(&format!(
                    "[InfiniteMoney] Customer base {} is no longer satisfied!",
                    customer_base_id
                ));
            }
            Event::ShopCheckout => {
                api.log_info("[InfiniteMoney] Shop checkout completed.");
            }
            Event::EmployeeHired => {
                api.log_info("[InfiniteMoney] New employee hired!");
            }
            Event::EmployeeFired => {
                api.log_info("[InfiniteMoney] Employee fired.");
            }
            Event::GameSaved => {
                api.log_info("[InfiniteMoney] Game saved.");
            }
            Event::GameLoaded => {
                api.log_info("[InfiniteMoney] Game loaded, re-applying infinite money.");
                api.set_player_money(999_999.0);
                api.set_player_xp(999_999.0);
                api.set_player_reputation(99_999.0);
            }
            Event::RackUnmounted => {
                api.log_info("[InfiniteMoney] Rack unmounted.");
            }
            Event::SwitchBroken => {
                api.log_warning("[InfiniteMoney] A network switch broke down!");
            }
            Event::SwitchRepaired => {
                api.log_info("[InfiniteMoney] Network switch repaired.");
            }
            Event::MonthEnded { month } => {
                api.log_info(&format!(
                    "[InfiniteMoney] Month {} ended — financial snapshot taken.",
                    month
                ));
            }
            Event::ShopItemAdded {
                item_id,
                price,
                item_type,
            } => {
                let type_name = match item_type {
                    1 => "Server 2U",
                    2 => "Server 7U",
                    3 => "Server 3U",
                    4 => "Switch",
                    5 => "Rack",
                    6 => "Cable Spinner",
                    7 => "Patch Panel",
                    8 => "SFP Module",
                    9 => "SFP Box",
                    _ => "Unknown",
                };
                api.log_info(&format!(
                    "[InfiniteMoney] Added to cart: {} (ID={}, ${}))",
                    type_name, item_id, price
                ));
            }
            Event::ShopCartCleared => {
                api.log_info("[InfiniteMoney] Shopping cart cleared.");
            }
            Event::ShopItemRemoved { uid } => {
                api.log_info(&format!(
                    "[InfiniteMoney] Item removed from cart (uid={})",
                    uid
                ));
            }
            Event::GameAutoSaved => {
                api.log_info("[InfiniteMoney] Game auto-saved.");
            }
            Event::WallPurchased => {
                api.log_info("[InfiniteMoney] Wall/room expansion purchased!");
            }
            Event::Unknown { event_id } => {
                api.log_info(&format!("[InfiniteMoney] Unknown event (id={})", event_id));
            }
        }
    }));

    match result {
        Ok(()) => {
            crash_log(&format!("[mod_on_event] <<< exit (event_id={})", event_id));
        }
        Err(e) => {
            crash_log(&format!(
                "[mod_on_event] caught panic (event_id={}): {}",
                event_id,
                panic_payload_to_string(&e)
            ));
        }
    }
}

#[no_mangle]
pub extern "C" fn mod_shutdown() {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if let Some(api) = api() {
            api.log_info("[InfiniteMoney] Mod shutting down. Goodbye!");
        } else {
            crash_log("[mod_shutdown] API not initialised, nothing to do");
        }
    }));

    if let Err(e) = result {
        crash_log(&format!(
            "[mod_shutdown] caught panic: {}",
            panic_payload_to_string(&e)
        ));
    }
}
