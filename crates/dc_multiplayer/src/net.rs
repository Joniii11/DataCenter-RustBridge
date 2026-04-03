//! Relay networking — WebSocket connection to the relay server with a single I/O thread.
//!
//! Previous design used two threads (reader + writer) sharing an `Arc<Mutex<WebSocket>>`.
//! This caused writer starvation: the reader held the lock ~100ms per cycle, starving the
//! writer for 10+ seconds. Now a single I/O thread owns the WebSocket exclusively —
//! it reads with a short timeout, then drains the outgoing packet queue, eliminating
//! all lock contention.

use crate::protocol::Message;
use dc_relay_proto::{self, RelayPacket};
use std::io;
use std::net::TcpStream;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::Arc;
use std::thread::{self, JoinHandle};
use std::time::{Duration, Instant};
use tungstenite::stream::MaybeTlsStream;
use tungstenite::WebSocket;

/// Convenience alias for the concrete WebSocket stream type.
type WsStream = WebSocket<MaybeTlsStream<TcpStream>>;

/// Events received from the relay server, delivered to the game thread.
pub enum RelayEvent {
    RoomCreated(String),
    JoinOk {
        host_steam_id: u64,
    },
    RoomNotFound,
    RoomFull,
    PeerJoined(u64),
    PeerLeft(u64),
    /// A game message from another player (already deserialized from the PeerData payload).
    GameMessage {
        sender: u64,
        message: Message,
    },
    /// Raw peer data that couldn't be deserialized, or a server-side error.
    Error(String),
    Disconnected,
}

/// Manages the WebSocket connection to the relay server.
pub struct RelayConnection {
    /// Send relay packets to the IO thread.
    tx: mpsc::Sender<RelayPacket>,
    /// Receive events from the IO thread.
    rx: mpsc::Receiver<RelayEvent>,
    /// Whether the connection is alive.
    alive: Arc<AtomicBool>,
    /// Background IO thread handle.
    _io_thread: Option<JoinHandle<()>>,
}

/// Set a read timeout on the underlying TCP stream so that `ws.read()` does not
/// block forever and the IO thread can send outgoing packets between reads.
fn set_read_timeout(ws: &mut WsStream, timeout: Duration) {
    match ws.get_mut() {
        MaybeTlsStream::Plain(tcp) => {
            let _ = tcp.set_read_timeout(Some(timeout));
        }
        MaybeTlsStream::NativeTls(tls) => {
            let _ = tls.get_mut().set_read_timeout(Some(timeout));
        }
        _ => {}
    }
}

impl RelayConnection {
    /// Connect to the relay server via WebSocket.
    ///
    /// `url` should be a full WebSocket URL, e.g. `"wss://dc-mp.joniii.dev"` or
    /// `"ws://192.99.16.77:9943"`.
    pub fn connect(url: &str) -> io::Result<Self> {
        dc_api::crash_log(&format!("[NET] Connecting to relay at {}", url));

        let (mut ws, _response) = tungstenite::connect(url).map_err(|e| {
            dc_api::crash_log(&format!("[NET] WebSocket connect failed: {}", e));
            io::Error::new(
                io::ErrorKind::ConnectionRefused,
                format!("WebSocket connect failed: {}", e),
            )
        })?;

        set_read_timeout(&mut ws, Duration::from_millis(10));

        let (event_tx, event_rx) = mpsc::channel::<RelayEvent>();
        let (packet_tx, packet_rx) = mpsc::channel::<RelayPacket>();
        let alive = Arc::new(AtomicBool::new(true));

        let alive_io = Arc::clone(&alive);
        let io_handle = thread::spawn(move || {
            io_loop(ws, event_tx, packet_rx, alive_io);
        });

        dc_api::crash_log("[NET] Relay connection established, IO thread started");

        Ok(Self {
            tx: packet_tx,
            rx: event_rx,
            alive,
            _io_thread: Some(io_handle),
        })
    }

    /// Send a relay packet to the server.
    pub fn send_packet(&self, packet: RelayPacket) -> bool {
        if !self.alive.load(Ordering::Relaxed) {
            return false;
        }
        self.tx.send(packet).is_ok()
    }

    /// Send a game message (serialized and wrapped in GameData).
    pub fn send_game_message(&self, msg: &Message) -> bool {
        let Some(payload) = msg.serialize() else {
            return false;
        };
        self.send_packet(RelayPacket::GameData { payload })
    }

    /// Drain all pending events from the IO thread (non-blocking).
    pub fn poll_events(&self) -> Vec<RelayEvent> {
        let mut events = Vec::new();
        while let Ok(event) = self.rx.try_recv() {
            events.push(event);
        }
        events
    }

    /// Check if the connection is still alive.
    pub fn is_alive(&self) -> bool {
        self.alive.load(Ordering::Relaxed)
    }

    /// Gracefully disconnect.
    pub fn disconnect(&self) {
        let _ = self.tx.send(RelayPacket::LeaveRoom);

        thread::sleep(Duration::from_millis(100));
        self.alive.store(false, Ordering::Relaxed);
    }
}

impl Drop for RelayConnection {
    fn drop(&mut self) {
        self.alive.store(false, Ordering::Relaxed);
    }
}

fn io_loop(
    mut ws: WsStream,
    event_tx: Sender<RelayEvent>,
    packet_rx: Receiver<RelayPacket>,
    alive: Arc<AtomicBool>,
) {
    let mut last_send = Instant::now();
    let heartbeat_interval = Duration::from_secs(15);

    while alive.load(Ordering::Relaxed) {
        match ws.read() {
            Ok(tungstenite::Message::Binary(data)) => {
                if let Some(packet) = dc_relay_proto::decode_packet(&data) {
                    if event_tx.send(packet_to_event(packet)).is_err() {
                        break; // game thread dropped the receiver
                    }
                }
            }
            Ok(tungstenite::Message::Close(_)) => {
                dc_api::crash_log("[NET] Received WebSocket Close frame");
                let _ = event_tx.send(RelayEvent::Disconnected);
                break;
            }
            Ok(_) => {}

            Err(tungstenite::Error::Io(ref e))
                if e.kind() == io::ErrorKind::WouldBlock || e.kind() == io::ErrorKind::TimedOut =>
            { //nop
            }

            Err(tungstenite::Error::ConnectionClosed) | Err(tungstenite::Error::AlreadyClosed) => {
                dc_api::crash_log("[NET] WebSocket connection closed");
                let _ = event_tx.send(RelayEvent::Disconnected);
                break;
            }

            Err(e) => {
                dc_api::crash_log(&format!("[NET] WebSocket read error: {}", e));
                let _ = event_tx.send(RelayEvent::Disconnected);
                break;
            }
        }

        let mut sent_something = false;
        let mut write_failed = false;

        while let Ok(packet) = packet_rx.try_recv() {
            if let Some(data) = dc_relay_proto::encode_ws(&packet) {
                if let Err(e) = ws.send(tungstenite::Message::Binary(data)) {
                    dc_api::crash_log(&format!("[NET] WebSocket write error: {}", e));
                    let _ = event_tx.send(RelayEvent::Disconnected);
                    write_failed = true;
                    break;
                }
                sent_something = true;
            }
        }

        if write_failed {
            break;
        }

        if sent_something {
            last_send = Instant::now();
        } else if last_send.elapsed() >= heartbeat_interval {
            if let Some(data) = dc_relay_proto::encode_ws(&RelayPacket::Heartbeat) {
                if let Err(e) = ws.send(tungstenite::Message::Binary(data)) {
                    dc_api::crash_log(&format!("[NET] Heartbeat write error: {}", e));
                    break;
                }
            }
            last_send = Instant::now();
        }
    }

    let _ = ws.close(None);
    for _ in 0..10 {
        match ws.read() {
            Ok(tungstenite::Message::Close(_)) | Err(_) => break,
            _ => {}
        }
    }

    alive.store(false, Ordering::Relaxed);
    dc_api::crash_log("[NET] IO thread exiting");
}

fn packet_to_event(packet: RelayPacket) -> RelayEvent {
    match packet {
        RelayPacket::RoomCreated { room_code } => RelayEvent::RoomCreated(room_code),
        RelayPacket::JoinOk { host_steam_id } => RelayEvent::JoinOk { host_steam_id },
        RelayPacket::RoomNotFound => RelayEvent::RoomNotFound,
        RelayPacket::RoomFull => RelayEvent::RoomFull,
        RelayPacket::PeerJoined { steam_id } => RelayEvent::PeerJoined(steam_id),
        RelayPacket::PeerLeft { steam_id } => RelayEvent::PeerLeft(steam_id),
        RelayPacket::PeerData {
            sender_steam_id,
            payload,
        } => match Message::deserialize(&payload) {
            Some(message) => RelayEvent::GameMessage {
                sender: sender_steam_id,
                message,
            },
            None => RelayEvent::Error(format!(
                "Failed to deserialize game message from {} ({} bytes)",
                sender_steam_id,
                payload.len()
            )),
        },
        RelayPacket::ServerError { message } => RelayEvent::Error(message),
        _ => RelayEvent::Error("Unexpected packet from server".to_string()),
    }
}
