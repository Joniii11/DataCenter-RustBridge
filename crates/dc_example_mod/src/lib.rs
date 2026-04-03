//! Infinite Money mod
use dc_api::*;

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

#[dc_api::on_scene_loaded]
fn scene_loaded(_api: &Api, _name: &str) {}

#[dc_api::on_event]
fn handle_event(api: &Api, event: Event) {
    match event {
        Event::GameLoaded => {
            api.log_info("[InfiniteMoney] Game loaded, re-applying infinite money.");
            api.set_player_money(999_999.0);
            api.set_player_xp(999_999.0);
            api.set_player_reputation(99_999.0);
        }
        _ => {}
    }
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    api.log_info("[InfiniteMoney] Goodbye!");
}
