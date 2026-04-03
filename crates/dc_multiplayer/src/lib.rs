//! Multiplayer mod — adds co-op to Data Center via TCP relay server.

mod net;
mod player;
mod protocol;

use dc_api::*;
use player::{PlayerTracker, RemotePlayerData};
use protocol::Message;
use std::ffi::{c_char, CString};
use std::sync::Mutex;
use std::sync::OnceLock;

const POSITION_SEND_INTERVAL: f32 = 0.05;
const HELLO_RETRY_INTERVAL: f32 = 2.0;
const HELLO_MAX_RETRIES: u32 = 15;
const DEFAULT_RELAY_URL: &str = "ws://192.99.16.77:9943"; // FIXME: Proper URL before release!

static STATE: OnceLock<Mutex<MultiplayerState>> = OnceLock::new();

struct MultiplayerState {
    tracker: PlayerTracker,
    /// Steam ID of the peer we're connected to (for 2-player)
    peer_id: u64,
    /// Whether we initiated the connection (host)
    is_host: bool,
    /// Our own steam ID
    my_id: u64,
    /// Timer for position sends
    pos_timer: f32,
    /// Whether handshake is complete
    connected: bool,
    /// Whether we're in "connecting" state (waiting for Welcome / room code / etc.)
    connecting: bool,
    /// Timer for Hello retry
    hello_retry_timer: f32,
    /// Number of Hello retries sent
    hello_retry_count: u32,
    /// Relay connection to the server
    relay: Option<net::RelayConnection>,
    /// Room code (set after RoomCreated or when joining)
    room_code: Option<String>,
    /// Cached CString for FFI return of room code
    room_code_cstr: Option<CString>,
    /// Whether we received JoinOk and sent our first Hello
    join_ok_received: bool,
}

impl MultiplayerState {
    fn new() -> Self {
        Self {
            tracker: PlayerTracker::new(),
            peer_id: 0,
            is_host: false,
            my_id: 0,
            pos_timer: 0.0,
            connected: false,
            connecting: false,
            hello_retry_timer: 0.0,
            hello_retry_count: 0,
            relay: None,
            room_code: None,
            room_code_cstr: None,
            join_ok_received: false,
        }
    }
}

fn with_state<F, R>(f: F) -> Option<R>
where
    F: FnOnce(&mut MultiplayerState) -> R,
{
    STATE
        .get()
        .and_then(|m| m.lock().ok())
        .map(|mut s| f(&mut s))
}

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

    let _ = STATE.set(Mutex::new(MultiplayerState::new()));

    // Steam ID is fetched lazily in update() since Steam isn't ready yet at mod load time
    api.log_info("[MP] Multiplayer mod initialized (relay mode). Use the UI to host or join.");
    true
}

#[dc_api::on_update]
fn update(api: &Api, dt: f32) {
    // ── Lazy Steam ID init ──
    let my_id = with_state(|s| s.my_id).unwrap_or(0);
    if my_id == 0 {
        if let Some(id) = api.steam_get_my_id() {
            with_state(|s| s.my_id = id);
            api.log_info(&format!("[MP] My Steam ID: {}", id));
        }
    }

    // ── Poll relay events ──
    let events: Vec<net::RelayEvent> = with_state(|s| {
        if let Some(ref relay) = s.relay {
            relay.poll_events()
        } else {
            Vec::new()
        }
    })
    .unwrap_or_default();

    for event in events {
        process_relay_event(api, event);
    }

    // ── Hello retry logic (client side only) ──
    let retry_needed = with_state(|s| {
        // Only retry if we're a client, connecting, received JoinOk, but not yet connected
        if s.is_host || !s.connecting || s.connected || !s.join_ok_received {
            return None;
        }

        s.hello_retry_timer += dt;
        if s.hello_retry_timer >= HELLO_RETRY_INTERVAL {
            s.hello_retry_timer = 0.0;
            s.hello_retry_count += 1;

            if s.hello_retry_count > HELLO_MAX_RETRIES {
                dc_api::crash_log("[MP] Hello retry limit reached, giving up.");
                s.connecting = false;
                s.join_ok_received = false;
                return None;
            }

            Some(s.hello_retry_count)
        } else {
            None
        }
    })
    .flatten();

    if let Some(count) = retry_needed {
        let msg = Message::Hello {
            player_name: "Player".to_string(),
            mod_version: "0.1.0".to_string(),
        };
        let ok = with_state(|s| {
            if let Some(ref relay) = s.relay {
                relay.send_game_message(&msg)
            } else {
                false
            }
        })
        .unwrap_or(false);

        if ok {
            dc_api::crash_log(&format!("[MP] Hello retry #{} sent (queued OK)", count));
        } else {
            dc_api::crash_log(&format!("[MP] Hello retry #{} FAILED (will retry)", count));
        }
    }

    // ── Position sending ──
    let (connected, _peer_id) = with_state(|s| (s.connected, s.peer_id)).unwrap_or((false, 0));

    if !connected {
        return;
    }

    let should_send = with_state(|s| {
        s.pos_timer += dt;
        if s.pos_timer >= POSITION_SEND_INTERVAL {
            s.pos_timer = 0.0;
            true
        } else {
            false
        }
    })
    .unwrap_or(false);

    if should_send {
        if let Some((x, y, z, ry)) = api.get_player_position() {
            let msg = Message::Position { x, y, z, rot_y: ry };
            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&msg);
                }
            });
        }
    }

    // ── Cleanup stale players ──
    with_state(|s| {
        let stale = s.tracker.cleanup_stale();
        for id in stale {
            dc_api::crash_log(&format!("[MP] player {} timed out", id));
        }
    });
}

fn process_relay_event(api: &Api, event: net::RelayEvent) {
    match event {
        net::RelayEvent::RoomCreated(code) => {
            dc_api::crash_log(&format!("[MP] Room created with code: {}", code));
            api.log_info(&format!("[MP] Room code: {}", code));
            with_state(|s| {
                s.room_code = Some(code);
            });
            // Host is now waiting for players to join
        }

        net::RelayEvent::JoinOk { host_steam_id } => {
            dc_api::crash_log(&format!(
                "[MP] Joined room, host steam ID: {}",
                host_steam_id
            ));
            api.log_info(&format!("[MP] Joined room (host: {})", host_steam_id));

            with_state(|s| {
                s.peer_id = host_steam_id;
                s.join_ok_received = true;
                s.hello_retry_timer = 0.0;
                s.hello_retry_count = 0;
            });

            // Send Hello to the host (via relay broadcast)
            let msg = Message::Hello {
                player_name: "Player".to_string(),
                mod_version: "0.1.0".to_string(),
            };
            let ok = with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&msg)
                } else {
                    false
                }
            })
            .unwrap_or(false);

            if ok {
                dc_api::crash_log("[MP] Sent Hello to host via relay");
            } else {
                dc_api::crash_log("[MP] Failed to send Hello to host via relay");
            }
        }

        net::RelayEvent::RoomNotFound => {
            dc_api::crash_log("[MP] Room not found!");
            api.log_error("[MP] Room not found. Check the room code and try again.");
            api.show_notification("Room not found!");
            do_disconnect_cleanup();
        }

        net::RelayEvent::RoomFull => {
            dc_api::crash_log("[MP] Room is full!");
            api.log_error("[MP] Room is full.");
            api.show_notification("Room is full!");
            do_disconnect_cleanup();
        }

        net::RelayEvent::PeerJoined(steam_id) => {
            dc_api::crash_log(&format!("[MP] Peer joined: {}", steam_id));
            api.log_info(&format!("[MP] Peer {} joined the room", steam_id));
            // They will send Hello, which triggers the handshake
        }

        net::RelayEvent::PeerLeft(steam_id) => {
            dc_api::crash_log(&format!("[MP] Peer left: {}", steam_id));
            with_state(|s| {
                s.tracker.remove_player(steam_id);
                if s.peer_id == steam_id {
                    s.peer_id = 0;
                    s.connected = false;
                }
            });
            api.log_info(&format!("[MP] Peer {} left", steam_id));
            api.show_notification("Player disconnected.");
        }

        net::RelayEvent::GameMessage { sender, message } => {
            handle_message(api, sender, message);
        }

        net::RelayEvent::Error(msg) => {
            dc_api::crash_log(&format!("[MP] Relay error: {}", msg));
            api.log_error(&format!("[MP] Error: {}", msg));
        }

        net::RelayEvent::Disconnected => {
            dc_api::crash_log("[MP] Disconnected from relay server");
            api.log_error("[MP] Lost connection to relay server.");
            api.show_notification("Disconnected from server.");
            do_disconnect_cleanup();
        }
    }
}

fn handle_message(api: &Api, sender: u64, msg: Message) {
    match msg {
        Message::Hello {
            player_name,
            mod_version,
        } => {
            api.log_info(&format!(
                "[MP] {} ({}) wants to connect (v{})",
                player_name, sender, mod_version
            ));

            with_state(|s| {
                s.peer_id = sender;
                s.connected = true;
                s.connecting = false;
                s.tracker.add_player(sender, player_name.clone());
            });

            // Send Welcome back via relay
            let my_name = "Host".to_string(); // TODO: get actual steam name
            let is_host = with_state(|s| s.is_host).unwrap_or(false);
            let welcome = Message::Welcome {
                player_name: my_name,
                is_host,
            };
            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&welcome);
                }
            });

            api.show_notification(&format!("{} joined!", player_name));
        }

        Message::Welcome {
            player_name,
            is_host: _,
        } => {
            api.log_info(&format!("[MP] Connected to {} ({})", player_name, sender));

            with_state(|s| {
                s.connected = true;
                s.connecting = false;
                s.join_ok_received = false;
                s.tracker.add_player(sender, player_name.clone());
            });

            api.show_notification(&format!("Connected to {}!", player_name));
        }

        Message::Position { x, y, z, rot_y } => {
            with_state(|s| {
                if !s.tracker.has_player(sender) {
                    // Got position from unknown player, auto-add them
                    s.tracker
                        .add_player(sender, format!("Player_{}", sender % 10000));
                }
                s.tracker.update_position(sender, x, y, z, rot_y);
            });
        }

        Message::Goodbye => {
            let name = with_state(|s| {
                s.tracker.remove_player(sender);
                if s.peer_id == sender {
                    s.peer_id = 0;
                    s.connected = false;
                }
                "Player".to_string() // TODO: get name before removal
            });
            api.log_info(&format!(
                "[MP] {} ({}) disconnected",
                name.unwrap_or_default(),
                sender
            ));
            api.show_notification("Player disconnected.");
        }

        Message::Ping(ts) => {
            let pong = Message::Pong(ts);
            with_state(|s| {
                if let Some(ref relay) = s.relay {
                    relay.send_game_message(&pong);
                }
            });
        }

        Message::Pong(_ts) => {
            // Could calculate RTT here
        }
    }
}

/// Internal cleanup helper — resets state without sending anything.
fn do_disconnect_cleanup() {
    with_state(|s| {
        if let Some(ref relay) = s.relay {
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
        s.tracker = player::PlayerTracker::new();
    });
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    with_state(|s| {
        if let Some(ref relay) = s.relay {
            if s.peer_id != 0 {
                relay.send_game_message(&Message::Goodbye);
            }
            relay.disconnect();
        }
        s.relay = None;
    });
    api.log_info("[MP] Multiplayer mod shutting down.");
}

// ═══════════════════════════════════════════════════════════════════════════════
// FFI exports
// ═══════════════════════════════════════════════════════════════════════════════

/// C# calls this to get remote player data for rendering.
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

/// Returns 1 if the relay connection is active (connected OR still connecting/hosting).
/// Use this instead of `mp_is_connected` to check whether the session is alive —
/// `mp_is_connected` only returns true after a peer completes the Hello handshake,
/// which causes false "disconnected" detection during the hosting/joining phase.
#[no_mangle]
pub extern "C" fn mp_is_relay_active() -> u32 {
    with_state(|s| {
        let relay_alive = s.relay.as_ref().map_or(false, |r| r.is_alive());
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

/// Get our own Steam ID (for display in UI).
#[no_mangle]
pub extern "C" fn mp_get_my_steam_id() -> u64 {
    with_state(|s| s.my_id).unwrap_or(0)
}

/// Host a game via relay server.
/// Uses the built-in relay address constants.
/// Returns: 1 = connecting to relay, 0 = failed
#[no_mangle]
pub extern "C" fn mp_host() -> i32 {
    let url = DEFAULT_RELAY_URL;

    let _api = match dc_api::mod_api() {
        Some(a) => a,
        None => return 0,
    };

    let my_id = with_state(|s| s.my_id).unwrap_or(0);

    // Connect to relay via WebSocket
    let conn = match net::RelayConnection::connect(url) {
        Ok(c) => c,
        Err(e) => {
            dc_api::crash_log(&format!("[MP] Failed to connect to relay: {}", e));
            return 0;
        }
    };

    // Send CreateRoom
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

/// Connect to a game via relay server with a room code.
/// `room_code`: pointer to UTF-8 string like `"ABC123"`
/// `room_code_len`: byte length of the room code string
/// Returns: 1 = connecting, 0 = failed
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
    });

    dc_api::crash_log(&format!("[MP] Joining room {} via relay at {}", code, url));
    1
}

/// Get the room code after hosting. Returns null-terminated C string pointer, or null.
/// The returned pointer is valid until the next call to this function or until disconnect.
#[no_mangle]
pub extern "C" fn mp_get_room_code() -> *const c_char {
    with_state(|s| {
        if let Some(ref code) = s.room_code {
            // Cache the CString so the pointer stays valid
            let cstr = CString::new(code.as_str()).unwrap_or_default();
            let ptr = cstr.as_ptr();
            s.room_code_cstr = Some(cstr);
            ptr
        } else {
            std::ptr::null()
        }
    })
    .unwrap_or(std::ptr::null())
}

/// Disconnect from current session.
#[no_mangle]
pub extern "C" fn mp_disconnect() -> i32 {
    with_state(|s| {
        if let Some(ref relay) = s.relay {
            // Send Goodbye to peer before disconnecting from relay
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
        s.tracker = player::PlayerTracker::new();
    });
    1
}
