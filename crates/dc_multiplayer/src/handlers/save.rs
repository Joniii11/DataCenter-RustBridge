use crate::protocol::Message;
use crate::state::*;

pub(super) fn handle_request_save(sender: u64) {
    dc_api::crash_log(&format!("[MP] Save requested by peer {}", sender));
    with_state(|s| {
        if s.session.is_host {
            s.save
                .transfers
                .entry(sender)
                .or_insert(crate::state::SaveTransferState { send_index: 0 });
        }
    });
}

pub(super) fn handle_save_offer(sender: u64, total_bytes: u32, chunk_count: u32, save_hash: u64) {
    dc_api::crash_log(&format!(
        "[MP] Received SaveOffer: {} bytes in {} chunks (hash: {:016x})",
        total_bytes, chunk_count, save_hash
    ));
    with_state(|s| {
        if !s.session.is_host {
            if s.save.local_hash != 0 && s.save.local_hash == save_hash {
                dc_api::crash_log(&format!(
                    "[MP] Save hash match! Local save is up to date (hash: {:016x})",
                    save_hash
                ));
                s.save.up_to_date = true;
                s.session.join_state = JoinState::SaveUpToDate;
                if let Some(ref relay) = s.session.relay {
                    relay.send_game_message_to(&Message::SaveSkip, sender);
                }
                return;
            }

            s.save.up_to_date = false;
            s.save.incoming_total = total_bytes;
            s.save.incoming_chunk_count = chunk_count;
            s.save.incoming_data = vec![0u8; total_bytes as usize];
            s.save.incoming_received = vec![false; chunk_count as usize];
            s.save.data_ready = None;
        }
    });
}

pub(super) fn handle_save_chunk(index: u32, data: Vec<u8>) {
    with_state(|s| {
        if s.session.is_host || s.save.up_to_date {
            return;
        }
        if index as usize >= s.save.incoming_received.len() {
            return;
        }

        let offset = index as usize * SAVE_CHUNK_SIZE;
        let end = (offset + data.len()).min(s.save.incoming_data.len());
        if offset < s.save.incoming_data.len() {
            s.save.incoming_data[offset..end].copy_from_slice(&data[..end - offset]);
        }
        s.save.incoming_received[index as usize] = true;

        let received_count = s.save.incoming_received.iter().filter(|&&r| r).count();
        dc_api::crash_log(&format!(
            "[MP] Save chunk {}/{} received ({} bytes)",
            received_count,
            s.save.incoming_chunk_count,
            data.len()
        ));

        if s.save.incoming_received.iter().all(|&r| r) {
            dc_api::crash_log(&format!(
                "[MP] All save chunks received! Total: {} bytes",
                s.save.incoming_total
            ));
            let complete = std::mem::take(&mut s.save.incoming_data);
            s.save.data_ready = Some(complete);
            s.save.incoming_received.clear();
            s.session.join_state = JoinState::SaveReady;
            dc_api::crash_log("[MP] join_state SaveReady");
        }
    });
}

pub(super) fn handle_save_skip(sender: u64) {
    dc_api::crash_log(&format!(
        "[MP] Peer {} says save is up to date, stopping transfer",
        sender
    ));
    with_state(|s| {
        if s.session.is_host {
            s.save.transfers.remove(&sender);

            if s.save.transfers.is_empty() {
                s.save.outgoing = None;
                s.save.chunk_count = 0;
            }
        }
    });
}
