//! Save sync FFI exports — called by C# to manage save file transfer.

use crate::protocol::Message;
use crate::state::*;

/// Tell the Rust side to NOT request save on the next connection.
/// Call this BEFORE mp_connect when doing an auto-reconnect.
#[no_mangle]
pub extern "C" fn mp_skip_next_save_request() {
    with_state(|s| {
        s.skip_next_save_request = true;
    });
}

/// Host: returns 1 if a client has requested save data and C# should provide it.
#[no_mangle]
pub extern "C" fn mp_should_send_save() -> u32 {
    with_state(|s| if s.save_requested { 1u32 } else { 0u32 }).unwrap_or(0)
}

/// Host: C# provides save file bytes. Rust will chunk and send them.
/// Returns 1 on success, 0 on failure.
#[no_mangle]
pub extern "C" fn mp_send_save_data(data: *const u8, len: u32) -> i32 {
    if data.is_null() || len == 0 {
        return 0;
    }

    let bytes = unsafe { std::slice::from_raw_parts(data, len as usize) }.to_vec();
    let total_bytes = bytes.len() as u32;
    let chunk_count = ((bytes.len() + SAVE_CHUNK_SIZE - 1) / SAVE_CHUNK_SIZE) as u32;
    let save_hash = compute_save_hash(&bytes);

    dc_api::crash_log(&format!(
        "[MP] Sending save data: {} bytes in {} chunks (hash: {:016x})",
        total_bytes, chunk_count, save_hash
    ));

    let offer = Message::SaveOffer {
        total_bytes,
        chunk_count,
        save_hash,
    };
    let sent = with_state(|s| {
        s.save_requested = false;
        s.save_outgoing = Some(bytes);
        s.save_send_index = 0;
        s.save_send_chunk_count = chunk_count;

        if let Some(ref relay) = s.relay {
            relay.send_game_message(&offer)
        } else {
            false
        }
    })
    .unwrap_or(false);

    if sent {
        1
    } else {
        0
    }
}

/// Client: returns 1 if complete save data from host is ready.
#[no_mangle]
pub extern "C" fn mp_has_pending_save() -> u32 {
    with_state(|s| {
        if s.save_data_ready.is_some() && !s.save_loaded {
            1u32
        } else {
            0u32
        }
    })
    .unwrap_or(0)
}

/// Client: returns the size in bytes of the pending save data (0 if none).
#[no_mangle]
pub extern "C" fn mp_get_save_data_size() -> u32 {
    with_state(|s| s.save_data_ready.as_ref().map_or(0u32, |d| d.len() as u32)).unwrap_or(0)
}

/// Client: copies pending save data into the provided buffer.
/// Returns number of bytes copied, or 0 if no data available.
#[no_mangle]
pub extern "C" fn mp_get_save_data(buf: *mut u8, max_len: u32) -> u32 {
    if buf.is_null() || max_len == 0 {
        return 0;
    }

    with_state(|s| {
        if let Some(ref data) = s.save_data_ready {
            let copy_len = data.len().min(max_len as usize);
            unsafe {
                std::ptr::copy_nonoverlapping(data.as_ptr(), buf, copy_len);
            }
            copy_len as u32
        } else {
            0u32
        }
    })
    .unwrap_or(0)
}

/// Client: signal that the save was loaded. Cleans up the pending data.
#[no_mangle]
pub extern "C" fn mp_save_load_complete() -> i32 {
    with_state(|s| {
        s.save_data_ready = None;
        s.save_loaded = true;
        dc_api::crash_log("[MP] Save load complete, pending data cleared");
    });
    1
}

/// C# provides the local save file bytes so Rust can compute and store the hash.
/// Call this before connecting to enable save versioning.
#[no_mangle]
pub extern "C" fn mp_set_local_save_hash(data: *const u8, len: u32) {
    if data.is_null() || len == 0 {
        with_state(|s| s.local_save_hash = 0);
        return;
    }
    let bytes = unsafe { std::slice::from_raw_parts(data, len as usize) };
    let hash = compute_save_hash(bytes);
    dc_api::crash_log(&format!(
        "[MP] Local save hash set: {:016x} ({} bytes)",
        hash, len
    ));
    with_state(|s| s.local_save_hash = hash);
}

/// Returns 1 if the host's save matches our local save (no download needed).
#[no_mangle]
pub extern "C" fn mp_is_save_up_to_date() -> u32 {
    with_state(|s| if s.save_up_to_date { 1u32 } else { 0u32 }).unwrap_or(0)
}

/// Returns save transfer progress: -1.0 if not transferring, 0.0..1.0 during transfer.
#[no_mangle]
pub extern "C" fn mp_get_save_transfer_progress() -> f32 {
    with_state(|s| {
        if s.save_up_to_date {
            return 1.0f32;
        }
        if s.save_incoming_chunk_count == 0 {
            return -1.0f32;
        }
        let received = s.save_incoming_received.iter().filter(|&&r| r).count() as f32;
        received / s.save_incoming_chunk_count as f32
    })
    .unwrap_or(-1.0)
}

/// Returns total bytes of the current incoming save transfer (0 if not active).
#[no_mangle]
pub extern "C" fn mp_get_save_transfer_total_bytes() -> u32 {
    with_state(|s| s.save_incoming_total).unwrap_or(0)
}
