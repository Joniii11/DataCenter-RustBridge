//! FFI exports for connection management, status queries, and join state.

use crate::net;
use crate::player::{PlayerTracker, RemotePlayerData};
use crate::protocol::Message;
use crate::state::*;
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
    with_state(|s| if s.connected { 1u32 } else { 0u32 }).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_is_relay_active() -> u32 {
    with_state(|s| {
        let relay_alive = s.relay.as_ref().is_some_and(|r| r.is_alive());
        if s.connected || s.connecting || (s.is_host && relay_alive) {
            1u32
        } else {
            0u32
        }
    })
    .unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_is_host() -> u32 {
    with_state(|s| if s.is_host { 1u32 } else { 0u32 }).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_player_count() -> u32 {
    with_state(|s| s.tracker.player_count() as u32).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_my_steam_id() -> u64 {
    with_state(|s| s.my_id).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_host() -> i32 {
    let url = DEFAULT_RELAY_URL;

    let _api = match dc_api::mod_api() {
        Some(a) => a,
        None => return 0,
    };

    let my_id = with_state(|s| s.my_id).unwrap_or(0);

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
        s.relay = Some(conn);
        s.is_host = true;
        s.connecting = true;
        s.connected = false;
        s.join_ok_received = false;
        s.hello_retry_timer = 0.0;
        s.hello_retry_count = 0;
        s.room_code = None;
        s.room_code_cstr = None;
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
        if let Some(ref relay) = s.relay {
            relay.disconnect();
        }
        s.relay = None;
    });

    let my_id = with_state(|s| s.my_id).unwrap_or(0);

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
        s.relay = Some(conn);
        s.is_host = false;
        s.connecting = true;
        s.connected = false;
        s.join_ok_received = false;
        s.hello_retry_timer = 0.0;
        s.hello_retry_count = 0;
        s.room_code = Some(code.clone());
        s.room_code_cstr = None;

        s.save_loaded = false;
        s.save_data_ready = None;
        s.save_incoming_total = 0;
        s.save_incoming_chunk_count = 0;
        s.save_incoming_data.clear();
        s.save_incoming_received.clear();
        s.save_up_to_date = false;
    });

    dc_api::crash_log(&format!("[MP] Joining room {} via relay at {}", code, url));
    1
}

/// Get the room code after hosting
#[no_mangle]
pub extern "C" fn mp_get_room_code() -> *const c_char {
    with_state(|s| {
        if let Some(ref code) = s.room_code {
            let cstr = std::ffi::CString::new(code.as_str()).unwrap_or_default();
            let ptr = cstr.as_ptr();
            s.room_code_cstr = Some(cstr);
            ptr
        } else {
            std::ptr::null()
        }
    })
    .unwrap_or(std::ptr::null())
}

/// Disconnect from current session
#[no_mangle]
pub extern "C" fn mp_disconnect() -> i32 {
    let entity_ids = with_state(|s| s.tracker.get_all_entity_ids()).unwrap_or_default();
    if let Some(api) = dc_api::mod_api() {
        for eid in entity_ids {
            api.destroy_entity(eid);
        }
    }

    with_state(|s| {
        if let Some(ref relay) = s.relay {
            if s.peer_id != 0 {
                relay.send_game_message(&Message::Goodbye);
            }
            relay.disconnect();
        }
        s.relay = None;
        s.peer_id = 0;
        s.connected = false;
        s.connecting = false;
        s.is_host = false;
        s.room_code = None;
        s.room_code_cstr = None;
        s.join_ok_received = false;
        s.hello_retry_timer = 0.0;
        s.hello_retry_count = 0;
        s.tracker = PlayerTracker::new();
        s.skip_next_save_request = false;

        s.save_transfers.clear();
        s.save_outgoing = None;
        s.save_chunk_count = 0;
        s.save_incoming_total = 0;
        s.save_incoming_chunk_count = 0;
        s.save_incoming_data.clear();
        s.save_incoming_received.clear();
        s.save_data_ready = None;
        s.save_loaded = false;
        s.save_up_to_date = false;
        s.join_state = JoinState::Idle;
    });
    1
}

/// Get the current join state as a u32
#[no_mangle]
pub extern "C" fn mp_get_join_state() -> u32 {
    with_state(|s| s.join_state as u32).unwrap_or(JoinState::Idle as u32)
}

/// Set the join state from C#
#[no_mangle]
pub extern "C" fn mp_set_join_state(state: u32) {
    with_state(|s| {
        let new_state = JoinState::from_u32(state);
        let old = s.join_state;
        dc_api::crash_log(&format!(
            "[MP] join_state {:?} → {:?} (set by C#)",
            old, new_state
        ));
        s.join_state = new_state;
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
            }
        }
    });
}
