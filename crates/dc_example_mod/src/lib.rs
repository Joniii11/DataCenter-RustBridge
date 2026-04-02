//! Infinite Money mod — example for the Data Center modloader.
//!
//! This mod demonstrates how to use `dc_api` proc macros to write a mod
//! with zero FFI boilerplate. Compare this to the old version which was 350+ lines!

use dc_api::events;
use dc_api::*;

// ── Mod entry point ─────────────────────────────────────────────────────────

#[dc_api::mod_entry(
    id = "infinite_money",
    name = "Infinite Money",
    version = "2.0.0",
    author = "Joniii",
    description = "Gives you $999,999 and logs game events. Example mod for the Data Center modloader."
)]
fn init(api: &Api) -> bool {
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
    }

    api.log_info("[InfiniteMoney] Listening for game events.");
    true
}

// ── Frame update ────────────────────────────────────────────────────────────

#[dc_api::on_update]
fn update(api: &Api, _dt: f32) {
    if api.get_player_money() < 999_999.0 {
        api.set_player_money(999_999.0);
    }
    if let Some(rep) = api.get_player_reputation() {
        if rep < 99_999.0 {
            api.set_player_reputation(99_999.0);
        }
    }
    if let Some(xp) = api.get_player_xp() {
        if xp < 999_999.0 {
            api.set_player_xp(999_999.0);
        }
    }
}

// ── Scene loaded ────────────────────────────────────────────────────────────

#[dc_api::on_scene_loaded]
fn scene_loaded(api: &Api, name: &str) {
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

// ── Game events ─────────────────────────────────────────────────────────────

#[dc_api::on_event]
fn handle_event(api: &Api, event: Event) {
    match event {
        Event::MoneyChanged {
            old_value,
            new_value,
            delta,
        } => {
            if delta.abs() > 0.01 && (old_value - 999_999.0).abs() > 1.0 {
                api.log_info(&format!(
                    "[InfiniteMoney] Money: ${:.2} -> ${:.2} ({:+.2})",
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
                "[InfiniteMoney] XP: {:.1} -> {:.1} (+{:.1})",
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
            api.log_info(&format!(
                "[InfiniteMoney] Server powered {}",
                if powered_on { "ON" } else { "OFF" }
            ));
        }
        Event::ServerBroken => api.log_warning("[InfiniteMoney] A server broke down!"),
        Event::ServerRepaired => api.log_info("[InfiniteMoney] Server repaired."),
        Event::ServerInstalled => api.log_info("[InfiniteMoney] Server installed in rack."),
        Event::CableConnected => api.log_info("[InfiniteMoney] Cable connected."),
        Event::CableDisconnected => api.log_info("[InfiniteMoney] Cable disconnected."),
        Event::ServerCustomerChanged { new_customer_id } => {
            api.log_info(&format!(
                "[InfiniteMoney] Server customer -> ID {}",
                new_customer_id
            ));
        }
        Event::ServerAppChanged { new_app_id } => {
            api.log_info(&format!("[InfiniteMoney] Server app -> ID {}", new_app_id));
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
                "[InfiniteMoney] Customer accepted (ID: {})",
                customer_id
            ));
        }
        Event::CustomerSatisfied { customer_base_id } => {
            api.log_info(&format!(
                "[InfiniteMoney] Customer {} satisfied!",
                customer_base_id
            ));
        }
        Event::CustomerUnsatisfied { customer_base_id } => {
            api.log_warning(&format!(
                "[InfiniteMoney] Customer {} unsatisfied!",
                customer_base_id
            ));
        }
        Event::ShopCheckout => api.log_info("[InfiniteMoney] Shop checkout."),
        Event::ShopItemAdded {
            item_id,
            price,
            item_type,
        } => {
            api.log_info(&format!(
                "[InfiniteMoney] Cart +{} (ID={}, ${})",
                events::item_type_name(item_type),
                item_id,
                price
            ));
        }
        Event::ShopCartCleared => api.log_info("[InfiniteMoney] Cart cleared."),
        Event::ShopItemRemoved { uid } => {
            api.log_info(&format!("[InfiniteMoney] Cart -uid={}", uid));
        }
        Event::EmployeeHired => api.log_info("[InfiniteMoney] Employee hired!"),
        Event::EmployeeFired => api.log_info("[InfiniteMoney] Employee fired."),
        Event::GameSaved => api.log_info("[InfiniteMoney] Game saved."),
        Event::GameLoaded => {
            api.log_info("[InfiniteMoney] Game loaded, re-applying infinite money.");
            api.set_player_money(999_999.0);
            api.set_player_xp(999_999.0);
            api.set_player_reputation(99_999.0);
        }
        Event::GameAutoSaved => api.log_info("[InfiniteMoney] Auto-saved."),
        Event::RackUnmounted => api.log_info("[InfiniteMoney] Rack unmounted."),
        Event::SwitchBroken => api.log_warning("[InfiniteMoney] Switch broke!"),
        Event::SwitchRepaired => api.log_info("[InfiniteMoney] Switch repaired."),
        Event::MonthEnded { month } => {
            api.log_info(&format!("[InfiniteMoney] Month {} ended.", month));
        }
        Event::WallPurchased => api.log_info("[InfiniteMoney] Wall purchased!"),
        Event::CustomEmployeeHired { ref employee_id } => {
            api.log_info(&format!(
                "[InfiniteMoney] Custom employee hired: {}",
                employee_id
            ));
        }
        Event::CustomEmployeeFired { ref employee_id } => {
            api.log_info(&format!(
                "[InfiniteMoney] Custom employee fired: {}",
                employee_id
            ));
        }
        Event::Unknown { event_id } => {
            api.log_info(&format!("[InfiniteMoney] Unknown event id={}", event_id));
        }
    }
}

// ── Shutdown ────────────────────────────────────────────────────────────────

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    api.log_info("[InfiniteMoney] Goodbye!");
}
