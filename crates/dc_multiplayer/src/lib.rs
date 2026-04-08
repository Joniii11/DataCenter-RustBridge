mod ffi;
mod handlers;
mod net;
mod objects;
mod player;
mod protocol;
mod state;
mod tick;
mod world;

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

#[dc_api::on_event]
fn handle_event(api: &Api, event: Event) {
    let connected = state::with_state(|s| s.session.connected).unwrap_or(false);
    if !connected {
        return;
    }

    let is_host = state::with_state(|s| s.session.is_host).unwrap_or(false);
    if !is_host {
        let loaded = state::with_state(|s| s.session.join_state == state::JoinState::Loaded)
            .unwrap_or(false);
        if !loaded {
            return;
        }
    }

    let executing = state::with_state(|s| s.executing_remote_action).unwrap_or(false);
    if executing {
        return;
    }

    let action = match event {
        Event::ServerInstalled {
            server_id,
            object_type,
            rack_position_uid,
        } => {
            state::with_state(|s| {
                s.carry.suppress_next_drop = true;
                s.carry.last_install_id = server_id.clone();
                s.carry.last_install_time = s.world_sync.game_time;
            });
            Some(protocol::WorldAction::InstalledInRack {
                object_id: server_id,
                object_type,
                rack_position_uid,
            })
        }
        Event::ObjectSpawned {
            object_id,
            object_type,
            prefab_id,
            pos,
            rot,
        } => Some(protocol::WorldAction::ObjectSpawned {
            object_id,
            object_type,
            prefab_id,
            pos_x: pos.0,
            pos_y: pos.1,
            pos_z: pos.2,
            rot_x: rot.0,
            rot_y: rot.1,
            rot_z: rot.2,
            rot_w: rot.3,
        }),

        Event::ObjectPickedUp { .. } | Event::ObjectDropped { .. } => None,
        _ => None,
    };

    if let Some(action) = action {
        send_world_action(api, action);
    }
}

pub(crate) fn send_world_action(_api: &Api, action: protocol::WorldAction) {
    let is_host = state::with_state(|s| s.session.is_host).unwrap_or(false);

    if is_host {
        let broadcast = protocol::Message::WorldActionBroadcast {
            action: action.clone(),
        };
        state::with_state(|s| {
            if let Some(ref relay) = s.session.relay {
                s.tracker.for_each_player(|player| {
                    relay.send_game_message_to(&broadcast, player.steam_id);
                });
            }
        });
        dc_api::crash_log(&format!(
            "[MP] Host broadcast world action: {} '{}'",
            action.tag(),
            action.object_id()
        ));
    } else {
        let seq = state::with_state(|s| s.world_sync.next_seq()).unwrap_or(0);
        if seq == 0 {
            dc_api::crash_log("[MP] Failed to get next seq for world action");
            return;
        }

        let rollback = world::RollbackInfo::None;

        state::with_state(|s| {
            s.world_sync.register_pending(seq, action.clone(), rollback);
        });

        let msg = protocol::Message::WorldActionMsg {
            seq,
            action: action.clone(),
        };
        state::with_state(|s| {
            if let Some(ref relay) = s.session.relay {
                relay.send_game_message(&msg);
            }
        });

        dc_api::crash_log(&format!(
            "[MP] Client sent WorldActionMsg seq={}: {} '{}'",
            seq,
            action.tag(),
            action.object_id()
        ));
    }
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    let entity_ids = state::with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            if s.session.peer_id != 0 {
                relay.send_game_message(&protocol::Message::Goodbye);
            }
            relay.disconnect();
        }
        s.session.relay = None;
        s.tracker.get_all_entity_ids()
    })
    .unwrap_or_default();

    for eid in entity_ids {
        api.destroy_entity(eid);
    }

    api.log_info("[MP] Multiplayer mod shutting down.");
}
