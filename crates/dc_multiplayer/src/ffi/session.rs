use crate::net;
use crate::player::{PlayerTracker, RemotePlayerData};
use crate::protocol::Message;
use crate::state::*;
use dc_api::world::registry::{reset_registry, with_registry_mut};
use std::ffi::c_char;

#[no_mangle]
pub extern "C" fn mp_get_remote_players(buf: *mut RemotePlayerData, max_count: u32) -> u32 {
    if buf.is_null() || max_count == 0 {
        return 0;
    }
    with_state(|s| {
        let slice = unsafe { std::slice::from_raw_parts_mut(buf, max_count as usize) };
        s.tracker.fill_ffi_buffer(slice) as u32
    })
    .unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_is_connected() -> u32 {
    with_state(|s| if s.session.connected { 1u32 } else { 0u32 }).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_is_relay_active() -> u32 {
    with_state(|s| {
        let relay_alive = s.session.relay.as_ref().is_some_and(|r| r.is_alive());
        if s.session.connected || s.session.connecting || (s.session.is_host && relay_alive) {
            1u32
        } else {
            0u32
        }
    })
    .unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_is_host() -> u32 {
    with_state(|s| if s.session.is_host { 1u32 } else { 0u32 }).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_player_count() -> u32 {
    with_state(|s| s.tracker.player_count() as u32).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_my_steam_id() -> u64 {
    with_state(|s| s.session.my_id).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_host() -> i32 {
    let url = DEFAULT_RELAY_URL;

    let _api = match dc_api::mod_api() {
        Some(a) => a,
        None => return 0,
    };

    let my_id = with_state(|s| s.session.my_id).unwrap_or(0);

    let conn = match net::RelayConnection::connect(url) {
        Ok(c) => c,
        Err(e) => {
            dc_api::crash_log(&format!("[MP] Failed to connect to relay: {}", e));
            return 0;
        }
    };

    if !conn.send_packet(dc_relay_proto::RelayPacket::CreateRoom { steam_id: my_id }) {
        dc_api::crash_log("[MP] Failed to send CreateRoom");
        return 0;
    }

    with_state(|s| {
        s.session.relay = Some(conn);
        s.session.is_host = true;
        s.session.connecting = true;
        s.session.connected = false;
        s.session.join_ok_received = false;
        s.session.hello_retry_timer = 0.0;
        s.session.hello_retry_count = 0;
        s.session.room_code = None;
        s.session.room_code_cstr = None;
    });

    dc_api::crash_log(&format!(
        "[MP] Hosting via relay at {}, waiting for room code...",
        url
    ));
    1
}

#[no_mangle]
pub extern "C" fn mp_connect(room_code: *const u8, room_code_len: u32) -> i32 {
    let url = DEFAULT_RELAY_URL;
    let code = unsafe {
        if room_code.is_null() {
            return 0;
        }
        let slice = std::slice::from_raw_parts(room_code, room_code_len as usize);
        match std::str::from_utf8(slice) {
            Ok(s) => s.trim().to_uppercase(),
            Err(_) => return 0,
        }
    };

    with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            relay.disconnect();
        }
        s.session.relay = None;
    });

    let my_id = with_state(|s| s.session.my_id).unwrap_or(0);

    let conn = match net::RelayConnection::connect(url) {
        Ok(c) => c,
        Err(e) => {
            dc_api::crash_log(&format!("[MP] Failed to connect to relay: {}", e));
            return 0;
        }
    };

    if !conn.send_packet(dc_relay_proto::RelayPacket::JoinRoom {
        room_code: code.clone(),
        steam_id: my_id,
    }) {
        dc_api::crash_log("[MP] Failed to send JoinRoom");
        return 0;
    }

    with_state(|s| {
        s.session.relay = Some(conn);
        s.session.is_host = false;
        s.session.connecting = true;
        s.session.connected = false;
        s.session.join_ok_received = false;
        s.session.hello_retry_timer = 0.0;
        s.session.hello_retry_count = 0;
        s.session.room_code = Some(code.clone());
        s.session.room_code_cstr = None;

        s.save.loaded = false;
        s.save.data_ready = None;
        s.save.incoming_total = 0;
        s.save.incoming_chunk_count = 0;
        s.save.incoming_data.clear();
        s.save.incoming_received.clear();
        s.save.up_to_date = false;
    });

    dc_api::crash_log(&format!("[MP] Joining room {} via relay at {}", code, url));
    1
}

#[no_mangle]
pub extern "C" fn mp_get_room_code() -> *const c_char {
    with_state(|s| {
        if let Some(ref code) = s.session.room_code {
            let cstr = std::ffi::CString::new(code.as_str()).unwrap_or_default();
            let ptr = cstr.as_ptr();
            s.session.room_code_cstr = Some(cstr);
            ptr
        } else {
            std::ptr::null()
        }
    })
    .unwrap_or(std::ptr::null())
}

#[no_mangle]
pub extern "C" fn mp_disconnect() -> i32 {
    let entity_ids = with_state(|s| s.tracker.get_all_entity_ids()).unwrap_or_default();
    if let Some(api) = dc_api::mod_api() {
        for eid in entity_ids {
            api.destroy_entity(eid);
        }
    }

    with_state(|s| {
        if let Some(ref relay) = s.session.relay {
            if s.session.peer_id != 0 {
                relay.send_game_message(&Message::Goodbye);
            }
            relay.disconnect();
        }
        s.session.relay = None;
        s.session.peer_id = 0;
        s.session.connected = false;
        s.session.connecting = false;
        s.session.is_host = false;
        s.session.room_code = None;
        s.session.room_code_cstr = None;
        s.session.join_ok_received = false;
        s.session.hello_retry_timer = 0.0;
        s.session.hello_retry_count = 0;
        s.session.rack_uids_ensured = false;
        s.session.registry_populated = false;
        s.tracker = PlayerTracker::new();
        s.save.skip_next_request = false;

        s.save.transfers.clear();
        s.save.outgoing = None;
        s.save.chunk_count = 0;
        s.save.incoming_total = 0;
        s.save.incoming_chunk_count = 0;
        s.save.incoming_data.clear();
        s.save.incoming_received.clear();
        s.save.data_ready = None;
        s.save.loaded = false;
        s.save.up_to_date = false;
        s.session.join_state = JoinState::Idle;
    });
    reset_registry();
    1
}

#[no_mangle]
pub extern "C" fn mp_get_join_state() -> u32 {
    with_state(|s| s.session.join_state as u32).unwrap_or(JoinState::Idle as u32)
}

#[no_mangle]
pub extern "C" fn mp_set_join_state(state: u32) {
    with_state(|s| {
        let new_state = JoinState::from_u32(state);
        let old = s.session.join_state;
        dc_api::crash_log(&format!(
            "[MP] join_state {:?} → {:?} (set by C#)",
            old, new_state
        ));
        s.session.join_state = new_state;
        if new_state == JoinState::Loaded && old != JoinState::Loaded {
            s.tracker.for_each_player_mut(|p| {
                p.entity_id = None;
            });
            dc_api::crash_log("[MP] Cleared entity IDs for respawn in new scene");

            if let Some(api) = dc_api::mod_api() {
                let assigned = api.world_ensure_rack_uids();
                dc_api::crash_log(&format!(
                    "[MP] Pre-assigned {} rack position UIDs after load",
                    assigned
                ));
                with_registry_mut(|r| r.populate_from_game(&api));
                dc_api::crash_log("[MP] Client: Object ID registry populated after load");
            }
        }
    });
}
