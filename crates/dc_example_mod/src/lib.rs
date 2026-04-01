//! Infinite Money mod - example for the Data Center modloader.
//! Sets money to $999,999 every frame and logs game events.

use dc_api::events::{self, Event};
use dc_api::*;
use std::ffi::c_char;
use std::sync::OnceLock;

static API: OnceLock<Api> = OnceLock::new();

fn api() -> &'static Api {
    API.get().expect("mod_init was not called")
}

#[no_mangle]
pub extern "C" fn mod_info() -> ModInfo {
    ModInfo::new(
        "infinite_money",
        "Infinite Money",
        "1.0.0",
        "Joniii",
        "Gives you $999,999 and logs game events for now ig",
    )
}

#[no_mangle]
pub extern "C" fn mod_init(game_api: &'static GameAPI) -> bool {
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
}

#[no_mangle]
pub extern "C" fn mod_update(_delta_time: f32) {
    let api = api();
    let money = api.get_player_money();
    if money < 999_999.0 {
        api.set_player_money(999_999.0);
    }
}

#[no_mangle]
pub extern "C" fn mod_on_scene_loaded(scene_name: *const c_char) {
    if scene_name.is_null() {
        return;
    }
    let name = unsafe { std::ffi::CStr::from_ptr(scene_name) }.to_string_lossy();
    let api = api();

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
}

#[no_mangle]
pub extern "C" fn mod_on_event(event_id: u32, event_data: *const u8, data_size: u32) {
    let Some(event) = events::decode(event_id, event_data, data_size) else {
        return;
    };

    let api = api();

    match event {
        Event::MoneyChanged {
            old_value,
            new_value,
            delta,
        } => {
            // skip logging our own infinite money writes
            if delta.abs() > 0.01 && (old_value - 999_999.0).abs() > 1.0 {
                api.log_info(&format!(
                    "[InfiniteMoney] Money changed: ${:.2} -> ${:.2} (delta: {:+.2})",
                    old_value, new_value, delta
                ));
            }
        }
        Event::XPChanged {
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
        }
        Event::Unknown { event_id } => {
            api.log_info(&format!("[InfiniteMoney] Unknown event (id={})", event_id));
        }
    }
}

#[no_mangle]
pub extern "C" fn mod_shutdown() {
    api().log_info("[InfiniteMoney] Mod shutting down. Goodbye!");
}
