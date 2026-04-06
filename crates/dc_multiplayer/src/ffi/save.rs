use crate::protocol::Message;
use crate::state::*;

#[no_mangle]
pub extern "C" fn mp_skip_next_save_request() {
    with_state(|s| {
        s.save.skip_next_request = true;
    });
}

#[no_mangle]
pub extern "C" fn mp_should_send_save() -> u32 {
    with_state(|s| {
        if !s.save.transfers.is_empty() && s.save.outgoing.is_none() {
            1u32
        } else {
            0u32
        }
    })
    .unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_send_save_data(data: *const u8, len: u32) -> i32 {
    if data.is_null() || len == 0 {
        return 0;
    }

    let bytes = unsafe { std::slice::from_raw_parts(data, len as usize) }.to_vec();
    let total_bytes = bytes.len() as u32;
    let chunk_count = bytes.len().div_ceil(SAVE_CHUNK_SIZE) as u32;
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
        s.save.outgoing = Some(bytes);
        s.save.chunk_count = chunk_count;

        let waiting: Vec<u64> = s.save.transfers.keys().copied().collect();
        let mut any_sent = false;

        for peer_id in waiting {
            if let Some(ref relay) = s.session.relay {
                if relay.send_game_message_to(&offer, peer_id) {
                    any_sent = true;
                    dc_api::crash_log(&format!(
                        "[MP] Sent SaveOffer to peer {} ({} bytes, {} chunks)",
                        peer_id, total_bytes, chunk_count
                    ));
                }
            }
            if let Some(t) = s.save.transfers.get_mut(&peer_id) {
                t.send_index = 0;
            }
        }

        any_sent
    })
    .unwrap_or(false);

    if sent {
        1
    } else {
        0
    }
}

#[no_mangle]
pub extern "C" fn mp_has_pending_save() -> u32 {
    with_state(|s| {
        if s.save.data_ready.is_some() && !s.save.loaded {
            1u32
        } else {
            0u32
        }
    })
    .unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_save_data_size() -> u32 {
    with_state(|s| s.save.data_ready.as_ref().map_or(0u32, |d| d.len() as u32)).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_save_data(buf: *mut u8, max_len: u32) -> u32 {
    if buf.is_null() || max_len == 0 {
        return 0;
    }

    with_state(|s| {
        if let Some(ref data) = s.save.data_ready {
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

#[no_mangle]
pub extern "C" fn mp_save_load_complete() -> i32 {
    with_state(|s| {
        s.save.data_ready = None;
        s.save.loaded = true;
        dc_api::crash_log("[MP] Save load complete, pending data cleared");
    });
    1
}

#[no_mangle]
pub extern "C" fn mp_set_local_save_hash(data: *const u8, len: u32) {
    if data.is_null() || len == 0 {
        with_state(|s| s.save.local_hash = 0);
        return;
    }
    let bytes = unsafe { std::slice::from_raw_parts(data, len as usize) };
    let hash = compute_save_hash(bytes);
    dc_api::crash_log(&format!(
        "[MP] Local save hash set: {:016x} ({} bytes)",
        hash, len
    ));
    with_state(|s| s.save.local_hash = hash);
}

#[no_mangle]
pub extern "C" fn mp_is_save_up_to_date() -> u32 {
    with_state(|s| if s.save.up_to_date { 1u32 } else { 0u32 }).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn mp_get_save_transfer_progress() -> f32 {
    with_state(|s| {
        if s.save.up_to_date {
            return 1.0f32;
        }
        if s.save.incoming_chunk_count == 0 {
            return -1.0f32;
        }
        let received = s.save.incoming_received.iter().filter(|&&r| r).count() as f32;
        received / s.save.incoming_chunk_count as f32
    })
    .unwrap_or(-1.0)
}

#[no_mangle]
pub extern "C" fn mp_get_save_transfer_total_bytes() -> u32 {
    with_state(|s| s.save.incoming_total).unwrap_or(0)
}
