//! Multiplayer mod — adds co-op to Data Center via relay server.

mod ffi;
mod handlers;
mod net;
mod player;
mod protocol;
mod save;
mod state;
mod tick;

use dc_api::*;

#[dc_api::mod_entry(
    id = "multiplayer",
    name = "Multiplayer",
    version = "0.1.0",
    author = "Joniii",
    description = "Co-op multiplayer for Data Center. Phase 1: see other players."
)]
fn init(api: &Api) -> bool {
    if api.version() < 7 {
        api.log_error("[MP] Requires API v7+! Update DataCenterModLoader.");
        return false;
    }

    state::init_state();
    api.log_info("[MP] Multiplayer mod initialized.");
    true
}

#[dc_api::on_update]
fn update(api: &Api, dt: f32) {
    tick::update(api, dt);
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    let entity_ids = state::with_state(|s| {
        if let Some(ref relay) = s.relay {
            if s.peer_id != 0 {
                relay.send_game_message(&protocol::Message::Goodbye);
            }
            relay.disconnect();
        }
        s.relay = None;
        s.tracker.get_all_entity_ids()
    })
    .unwrap_or_default();

    for eid in entity_ids {
        api.destroy_entity(eid);
    }

    api.log_info("[MP] Multiplayer mod shutting down.");
}
